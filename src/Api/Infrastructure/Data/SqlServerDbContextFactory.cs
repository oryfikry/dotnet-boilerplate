using Api.Common.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Infrastructure.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> for SQL Server migrations.
/// See ADR-0001.
/// </summary>
internal sealed class SqlServerDbContextFactory : IDesignTimeDbContextFactory<SqlServerDbContext>
{
    public const string FallbackConnectionString =
        "Server=localhost,1433;Database=pbac;User Id=sa;Password=Pbac!Local123;TrustServerCertificate=True";

    public SqlServerDbContext CreateDbContext(string[] args)
    {
        var configuration = DesignTimeConfiguration.Build();
        var connectionString = DesignTimeConfiguration.ResolveConnectionString(
            configuration, DatabaseProvider.SqlServer, FallbackConnectionString);

        var options = new DbContextOptionsBuilder<SqlServerDbContext>()
            .UseSqlServer(connectionString, o => o.MigrationsAssembly(typeof(SqlServerDbContext).Assembly.FullName))
            .Options;

        return new SqlServerDbContext(options, new NullRequestContext());
    }
}
