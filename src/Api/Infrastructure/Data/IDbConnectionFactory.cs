using System.Data.Common;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Infrastructure.Data;

/// <summary>
/// Factory that produces fresh <see cref="DbConnection"/> instances for
/// Dapper-based query handlers.
///
/// Per PRD v2.1 §6 directive #3, query handlers consume this factory and
/// open the connection inside a <c>using</c> block. The factory does
/// <em>not</em> share its connection with <see cref="AppDbContext"/>
/// (PRD §9.10).
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>Open a fresh database connection. Caller owns disposal.</summary>
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

internal sealed class NpgsqlConnectionFactory(IOptions<DatabaseOptions> options)
    : IDbConnectionFactory
{
    private readonly string _connectionString = options.Value.ConnectionString
        ?? throw new InvalidOperationException(
            "Database:ConnectionString is not configured. " +
            "Set ConnectionStrings:Postgres in appsettings.{Environment}.json or via user secrets.");

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

/// <summary>
/// Strongly-typed binding for the database connection string. Bound from
/// <c>ConnectionStrings:Postgres</c>.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string? ConnectionString { get; set; }
}
