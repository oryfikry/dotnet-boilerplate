using Api.Common.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Api.Infrastructure.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> for PostgreSQL migrations.
///
/// Bound to the base <see cref="AppDbContext"/> directly (not a subclass) to
/// preserve the historical migration <c>20260516074807_Initial</c> at
/// <c>Infrastructure/Data/Migrations/</c>. See ADR-0001 for rationale.
///
/// Reads <c>ConnectionStrings:Postgres</c>, then <c>ConnectionStrings:Default</c>,
/// falling back to a local docker-compose default.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string FallbackConnectionString =
        "Host=localhost;Port=5433;Database=pbac;Username=pbac;Password=pbac";

    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = DesignTimeConfiguration.Build();
        var connectionString = DesignTimeConfiguration.ResolveConnectionString(
            configuration, DatabaseProvider.Postgres, FallbackConnectionString);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options, new NullRequestContext());
    }
}
