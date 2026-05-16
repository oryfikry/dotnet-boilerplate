using Api.Common.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Api.Infrastructure.Data;

/// <summary>
/// Design-time factory used by the EF Core CLI (<c>dotnet ef</c>) to construct
/// an <see cref="AppDbContext"/> outside the host pipeline.
///
/// Reads <c>ConnectionStrings:Postgres</c> from <c>appsettings.json</c> /
/// <c>appsettings.Development.json</c> / environment variables / user secrets.
/// Falls back to a local docker-compose default so newcomers can run
/// <c>dotnet ef migrations add</c> without configuration.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=pbac;Username=pbac;Password=pbac";

    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
        {
            // When invoked from the repo root via `--project src\Api`, the
            // EF tools set CWD to src\Api; but if invoked from elsewhere fall
            // back to that path.
            var apiDir = Path.Combine(basePath, "src", "Api");
            if (Directory.Exists(apiDir)) basePath = apiDir;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<AppDbContext>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres") ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options, new NullRequestContext());
    }
}
