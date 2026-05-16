using System.Threading.RateLimiting;
using Api.Infrastructure.Data;
using Api.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.IntegrationTests.Infrastructure;

/// <summary>
/// Pure-integration <see cref="WebApplicationFactory{TEntryPoint}"/> backed by
/// real Postgres in a Testcontainer (PRD v2.2 §11 M4, §6 directive #4).
///
/// <para>
/// Redis is intentionally <em>not</em> containerized for tests: omitting the
/// <see cref="IConnectionMultiplexer"/> registration drives <c>HybridCacheService</c>
/// through its L1 + L3 path, which is the same path Production hits when Redis
/// is degraded (PRD §4.2 graceful-degradation). This keeps tests fast and
/// deterministic while still exercising <c>InvalidatesCache</c>, the cache
/// behavior pipeline, and read-your-writes.
/// </para>
///
/// <para>
/// The factory also (a) overrides <c>Database:Provider=Postgres</c>, (b)
/// supplies a deterministic JWT signing key, (c) replaces the <c>"sensitive"</c>
/// rate-limit policy with a no-op so login/refresh tests can run in tight
/// loops, and (d) applies migrations against the container DB on first
/// resolve.
/// </para>
/// </summary>
public sealed class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestJwtSigningKey = "integration-test-jwt-signing-key-do-not-use-in-prod-1234567890!!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("pbac_tests")
        .WithUsername("pbac")
        .WithPassword("pbac")
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("IntegrationTesting");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Strip the app's own appsettings sources to keep test configuration
            // deterministic. Note: this fires AFTER AddAppDatabase has already
            // read Database:Provider during top-level Program.cs execution,
            // so we additionally re-wire the DbContext below in
            // ConfigureTestServices.
            var existing = config.Sources
                .OfType<Microsoft.Extensions.Configuration.Json.JsonConfigurationSource>()
                .ToList();
            foreach (var src in existing)
            {
                config.Sources.Remove(src);
            }

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Postgres",
                ["ConnectionStrings:Postgres"] = PostgresConnectionString,
                ["Jwt:SigningKey"] = TestJwtSigningKey,
                ["Jwt:Issuer"] = "pbac-tests",
                ["Jwt:Audience"] = "pbac-tests",
                ["Cache:RedisConnectionString"] = null,
                ["RateLimiting:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Drop whatever DbContext + connection factory the entry point
            // wired up at startup (default: Sqlite from appsettings.json) and
            // replace with a Postgres-bound stack pointed at the Testcontainer.
            RemoveAll<DbContextOptions<AppDbContext>>(services);
            RemoveAll<DbContextOptions<SqliteDbContext>>(services);
            RemoveAll<DbContextOptions<SqlServerDbContext>>(services);
            RemoveAll<DbContextOptions<MySqlDbContext>>(services);
            RemoveAll<AppDbContext>(services);
            RemoveAll<SqliteDbContext>(services);
            RemoveAll<SqlServerDbContext>(services);
            RemoveAll<MySqlDbContext>(services);
            RemoveAll<IDbConnectionFactory>(services);
            RemoveAll<IConnectionMultiplexer>(services);

            services.AddDbContext<AppDbContext>(opts =>
                opts.UseNpgsql(PostgresConnectionString,
                    npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

            services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

            // Re-bind DatabaseOptions so anything pulling it in DI sees the
            // Postgres connection (NpgsqlConnectionFactory reads it).
            services.Configure<DatabaseOptions>(o =>
            {
                o.Provider = DatabaseProvider.Postgres;
                o.ConnectionString = PostgresConnectionString;
            });

            // Disable rate limiting for tests (the "sensitive" 10/min policy
            // would otherwise return 429 across login/refresh test suites that
            // share the same test-server IP partition). The host registered
            // policies during Program.cs; we replace the IOptionsFactory so a
            // fresh, empty RateLimiterOptions is materialized when middleware
            // resolves it.
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.RemoveAll<IPostConfigureOptions<RateLimiterOptions>>();
            services.AddSingleton<IConfigureOptions<RateLimiterOptions>>(_ =>
                new ConfigureNamedOptions<RateLimiterOptions>(Microsoft.Extensions.Options.Options.DefaultName, opts =>
                {
                    opts.GlobalLimiter = null;
                    opts.AddPolicy("sensitive", _ => RateLimitPartition.GetNoLimiter("noop"));
                }));
        });
    }

    private static void RemoveAll<T>(IServiceCollection services)
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in descriptors)
        {
            services.Remove(d);
        }
    }

    /// <summary>
    /// Apply EF Core migrations once the host has been built and the DB
    /// container is ready. Returns the seeded user's id.
    /// </summary>
    public async Task EnsureMigratedAsync(CancellationToken ct = default)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Wraps <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/>
    /// without auto-redirect so 401/403/404 responses flow back unchanged.
    /// </summary>
    public HttpClient CreateRawClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });
}
