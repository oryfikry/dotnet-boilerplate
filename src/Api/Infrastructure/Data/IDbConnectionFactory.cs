using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;

namespace Api.Infrastructure.Data;

/// <summary>
/// Factory that produces fresh <see cref="DbConnection"/> instances for
/// Dapper-based query handlers.
///
/// Per PRD v2.2 §6 directive #3, query handlers consume this factory and
/// open the connection inside a <c>using</c> block. The factory does
/// <em>not</em> share its connection with <see cref="AppDbContext"/>
/// (PRD §9.10).
///
/// Concrete implementation is selected at startup based on
/// <see cref="DatabaseOptions.Provider"/> — see ADR-0001.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>Open a fresh database connection. Caller owns disposal.</summary>
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

internal abstract class DbConnectionFactoryBase : IDbConnectionFactory
{
    protected readonly string ConnectionString;

    protected DbConnectionFactoryBase(string connectionString)
    {
        ConnectionString = connectionString
            ?? throw new InvalidOperationException(
                "Database:ConnectionString is not configured. " +
                "Set ConnectionStrings:Default (or ConnectionStrings:{Provider}) " +
                "in appsettings.{Environment}.json or via user secrets.");
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    protected abstract DbConnection CreateConnection();
}

internal sealed class SqliteConnectionFactory(IOptions<DatabaseOptions> options)
    : DbConnectionFactoryBase(options.Value.ConnectionString ?? throw new InvalidOperationException(
        "Database:ConnectionString is not configured for SQLite provider. " +
        "Set ConnectionStrings:Sqlite or ConnectionStrings:Default in appsettings.json."))
{
    protected override DbConnection CreateConnection() => new SqliteConnection(ConnectionString);
}

internal sealed class NpgsqlConnectionFactory(IOptions<DatabaseOptions> options)
    : DbConnectionFactoryBase(options.Value.ConnectionString ?? throw new InvalidOperationException(
        "Database:ConnectionString is not configured for PostgreSQL provider. " +
        "Set ConnectionStrings:Postgres or ConnectionStrings:Default in appsettings.json."))
{
    protected override DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);
}

internal sealed class SqlServerConnectionFactory(IOptions<DatabaseOptions> options)
    : DbConnectionFactoryBase(options.Value.ConnectionString ?? throw new InvalidOperationException(
        "Database:ConnectionString is not configured for SQL Server provider. " +
        "Set ConnectionStrings:SqlServer or ConnectionStrings:Default in appsettings.json."))
{
    protected override DbConnection CreateConnection() => new SqlConnection(ConnectionString);
}

internal sealed class MySqlConnectionFactory(IOptions<DatabaseOptions> options)
    : DbConnectionFactoryBase(options.Value.ConnectionString ?? throw new InvalidOperationException(
        "Database:ConnectionString is not configured for MySQL provider. " +
        "Set ConnectionStrings:MySql or ConnectionStrings:Default in appsettings.json."))
{
    protected override DbConnection CreateConnection() => new MySqlConnection(ConnectionString);
}

/// <summary>
/// Strongly-typed binding for the database configuration.
/// Bound from the <c>Database</c> section, with the connection string
/// resolved at startup from <c>ConnectionStrings:{Provider}</c> or
/// <c>ConnectionStrings:Default</c>.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    public string? ConnectionString { get; set; }
}

/// <summary>
/// Supported database providers. See ADR-0001.
/// <para>
/// <strong>Do not reorder</strong> — integer values may be persisted in logs,
/// config, or audit trails. Add new providers at the end.
/// </para>
/// </summary>
public enum DatabaseProvider
{
    Sqlite = 0,
    Postgres = 1,
    SqlServer = 2,
    MySql = 3,
}
