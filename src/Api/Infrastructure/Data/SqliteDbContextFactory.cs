using Api.Common.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Infrastructure.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> for SQLite migrations.
/// Bound to the <see cref="SqliteDbContext"/> subclass so its model
/// snapshot is independent of the other providers (ADR-0001).
/// </summary>
internal sealed class SqliteDbContextFactory : IDesignTimeDbContextFactory<SqliteDbContext>
{
    public const string FallbackConnectionString = "Data Source=App_Data/pbac.db";

    public SqliteDbContext CreateDbContext(string[] args)
    {
        var configuration = DesignTimeConfiguration.Build();
        var connectionString = DesignTimeConfiguration.ResolveConnectionString(
            configuration, DatabaseProvider.Sqlite, FallbackConnectionString);

        var options = new DbContextOptionsBuilder<SqliteDbContext>()
            .UseSqlite(connectionString, o => o.MigrationsAssembly(typeof(SqliteDbContext).Assembly.FullName))
            .Options;

        return new SqliteDbContext(options, new NullRequestContext());
    }
}
