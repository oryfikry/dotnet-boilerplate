using Api.Common.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Api.Infrastructure.Data;

/// <summary>
/// Shared base for design-time <see cref="IDesignTimeDbContextFactory{T}"/>
/// implementations. Each provider has its own factory so <c>dotnet ef</c>
/// can target it with <c>--context</c> and emit migrations to a dedicated
/// folder. See ADR-0001.
/// </summary>
internal static class DesignTimeConfiguration
{
    public static IConfiguration Build()
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

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<AppDbContext>(optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>
    /// Resolves the connection string for the given provider, preferring
    /// <c>ConnectionStrings:{provider}</c> then falling back to
    /// <c>ConnectionStrings:Default</c> then a hard-coded provider default.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration config, DatabaseProvider provider, string fallback)
        => config.GetConnectionString(provider.ToString())
           ?? config.GetConnectionString("Default")
           ?? fallback;
}
