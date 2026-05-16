using Api.Common.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Infrastructure.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> for MySQL migrations
/// (Oracle MySql.EntityFrameworkCore). See ADR-0001.
/// </summary>
internal sealed class MySqlDbContextFactory : IDesignTimeDbContextFactory<MySqlDbContext>
{
    public const string FallbackConnectionString =
        "Server=localhost;Port=3306;Database=pbac;User=pbac;Password=pbac";

    public MySqlDbContext CreateDbContext(string[] args)
    {
        var configuration = DesignTimeConfiguration.Build();
        var connectionString = DesignTimeConfiguration.ResolveConnectionString(
            configuration, DatabaseProvider.MySql, FallbackConnectionString);

        var options = new DbContextOptionsBuilder<MySqlDbContext>()
            .UseMySQL(connectionString, o => o.MigrationsAssembly(typeof(MySqlDbContext).Assembly.FullName))
            .Options;

        return new MySqlDbContext(options, new NullRequestContext());
    }
}
