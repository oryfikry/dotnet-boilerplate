using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Common.Responses;
using Api.Features.Auth.Login;
using Api.Infrastructure.Caching;
using Api.Infrastructure.Data;
using Api.Infrastructure.Data.Entities;
using Api.Infrastructure.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Xunit;

namespace Api.IntegrationTests.Infrastructure;

/// <summary>
/// Base class every integration test inherits from.
///
/// <para>
/// Lifecycle: each test class shares the <see cref="IntegrationTestWebAppFactory"/>
/// (and therefore the Postgres container). <see cref="InitializeAsync"/> resets
/// the database between tests via Respawn so tests stay isolated without
/// paying the cost of new containers.
/// </para>
///
/// <para>
/// Helpers expose the slice's public seams: <see cref="Sender"/> (MediatR),
/// <see cref="Db"/> (write-side EF), <see cref="Cache"/> (HybridCacheService),
/// <see cref="Hasher"/>, and <see cref="CreateClient"/> for raw HTTP.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly IntegrationTestWebAppFactory _factory;
    private IServiceScope _scope = default!;
    private static Respawner? _respawner;
    private static readonly SemaphoreSlim _migrateGate = new(1, 1);
    private static bool _migrated;

    protected BaseIntegrationTest(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
    }

    protected IntegrationTestWebAppFactory Factory => _factory;
    protected IServiceProvider Services => _scope.ServiceProvider;
    protected ISender Sender => Services.GetRequiredService<ISender>();
    protected AppDbContext Db => Services.GetRequiredService<AppDbContext>();
    protected ICacheService Cache => Services.GetRequiredService<ICacheService>();
    protected IPasswordHasher Hasher => Services.GetRequiredService<IPasswordHasher>();
    protected IJwtTokenService Tokens => Services.GetRequiredService<IJwtTokenService>();

    public async Task InitializeAsync()
    {
        await EnsureMigrationsAndRespawnerAsync();
        await ResetDatabaseAsync();
        _scope = _factory.Services.CreateScope();
    }

    public Task DisposeAsync()
    {
        _scope?.Dispose();
        return Task.CompletedTask;
    }

    private async Task EnsureMigrationsAndRespawnerAsync()
    {
        await _migrateGate.WaitAsync();
        try
        {
            if (_migrated) return;

            await _factory.EnsureMigratedAsync();

            await using var conn = new NpgsqlConnection(_factory.PostgresConnectionString);
            await conn.OpenAsync();
            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = [new("__EFMigrationsHistory")],
            });

            _migrated = true;
        }
        finally
        {
            _migrateGate.Release();
        }
    }

    protected async Task ResetDatabaseAsync()
    {
        if (_respawner is null) return;
        await using var conn = new NpgsqlConnection(_factory.PostgresConnectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    /// <summary>
    /// Inserts a user (active by default) with the given permissions and
    /// returns the persisted <see cref="User"/>. Permission rows are created
    /// idempotently so multiple tests can reuse the same permission strings.
    /// </summary>
    protected async Task<User> SeedUserAsync(
        string email,
        string password = "Test123!Test123!",
        bool isActive = true,
        params string[] permissions)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = Hasher.Hash(password),
            IsActive = isActive,
        };
        Db.Users.Add(user);

        foreach (var name in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var perm = await Db.Permissions.FirstOrDefaultAsync(p => p.Name == name)
                       ?? new Permission
                       {
                           Id = Guid.CreateVersion7(),
                           Name = name,
                           Description = $"Test permission: {name}",
                       };
            if (Db.Entry(perm).State == EntityState.Detached)
            {
                Db.Permissions.Add(perm);
            }
            Db.UserPermissions.Add(new UserPermission
            {
                UserId = user.Id,
                PermissionId = perm.Id,
            });
        }

        await Db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Issues an access token directly via the <see cref="IJwtTokenService"/>
    /// so HTTP tests don't have to round-trip through <c>POST /auth/login</c>.
    /// </summary>
    protected string IssueAccessToken(User user, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(user);
        var perms = permissions.Length > 0
            ? permissions
            : Db.UserPermissions
                .Where(up => up.UserId == user.Id)
                .Include(up => up.Permission)
                .Select(up => up.Permission!.Name)
                .ToArray();
        return Tokens.IssueAccessToken(user.Id, perms).Token;
    }

    /// <summary>Convenience: HttpClient with bearer token preset.</summary>
    protected HttpClient CreateClient(string? bearerToken = null)
    {
        var client = _factory.CreateRawClient();
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return client;
    }

    /// <summary>
    /// Login over HTTP (full pipeline), returning the parsed <see cref="LoginResponse"/>.
    /// </summary>
    protected async Task<LoginResponse> LoginHttpAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginCommand(email, password));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOpts);
        return envelope!.Data!;
    }

    protected static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
