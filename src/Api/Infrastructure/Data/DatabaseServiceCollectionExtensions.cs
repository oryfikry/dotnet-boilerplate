using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Data;

/// <summary>
/// Wires <see cref="AppDbContext"/> and <see cref="IDbConnectionFactory"/>
/// based on the configured provider.
///
/// <para>
/// Configuration shape (any of these resolves a connection string, in order):
/// </para>
/// <code>
/// {
///   "Database": { "Provider": "Sqlite" },
///   "ConnectionStrings": {
///     "Sqlite":    "Data Source=App_Data/pbac.db",
///     "Postgres":  "Host=localhost;Port=5433;Database=pbac;Username=pbac;Password=pbac",
///     "SqlServer": "Server=localhost,1433;Database=pbac;User Id=sa;Password=...;TrustServerCertificate=True",
///     "MySql":     "Server=localhost;Port=3306;Database=pbac;User=pbac;Password=pbac",
///     "Default":   "..."   // optional fallback used when ConnectionStrings:{Provider} is missing
///   }
/// }
/// </code>
///
/// See ADR-0001 for the multi-provider rationale and per-provider migration layout.
/// </summary>
public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddAppDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var dbSection = configuration.GetSection(DatabaseOptions.SectionName);
        var provider = ParseProvider(dbSection["Provider"]);
        var connectionString = ResolveConnectionString(configuration, provider, environment);

        services
            .AddOptions<DatabaseOptions>()
            .Configure(o =>
            {
                o.Provider = provider;
                o.ConnectionString = connectionString;
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"Database connection string is not configured for provider '{provider}'. " +
                $"Set ConnectionStrings:{provider} or ConnectionStrings:Default in appsettings.json.")
            .ValidateOnStart();

        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                ConfigureSqliteServices(services, connectionString, environment);
                break;

            case DatabaseProvider.Postgres:
                ConfigurePostgresServices(services, connectionString, environment);
                break;

            case DatabaseProvider.SqlServer:
                ConfigureSqlServerServices(services, connectionString, environment);
                break;

            case DatabaseProvider.MySql:
                ConfigureMySqlServices(services, connectionString, environment);
                break;

            default:
                throw new InvalidOperationException($"Unsupported database provider: {provider}.");
        }

        return services;
    }

    private static DatabaseProvider ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DatabaseProvider.Sqlite; // ADR-0001: SQLite is the default.
        }

        if (Enum.TryParse<DatabaseProvider>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Unknown Database:Provider value '{value}'. " +
            $"Allowed: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}.");
    }

    private static string ResolveConnectionString(
        IConfiguration configuration, DatabaseProvider provider, IHostEnvironment environment)
    {
        var perProvider = configuration.GetConnectionString(provider.ToString());
        if (!string.IsNullOrWhiteSpace(perProvider))
        {
            return perProvider;
        }

        var defaultCs = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(defaultCs))
        {
            return defaultCs;
        }

        // Allow the SQLite default to "just work" out of the box in any
        // environment: zero-config dev experience (ADR-0001).
        if (provider == DatabaseProvider.Sqlite)
        {
            return SqliteDbContextFactory.FallbackConnectionString;
        }

        throw new InvalidOperationException(
            $"No connection string configured for provider '{provider}'. " +
            $"Tried: ConnectionStrings:{provider}, ConnectionStrings:Default. " +
            $"Set one of these in appsettings.{environment.EnvironmentName}.json or via user secrets.");
    }

    private static void ConfigureSqlite(DbContextOptionsBuilder opts, string cs, IHostEnvironment env)
    {
        opts.UseSqlite(cs, sqlite =>
            sqlite.MigrationsAssembly(typeof(SqliteDbContext).Assembly.FullName));
        ApplyDevDiagnostics(opts, env);
    }

    private static void ConfigurePostgres(DbContextOptionsBuilder opts, string cs, IHostEnvironment env)
    {
        opts.UseNpgsql(cs, npg =>
            npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        ApplyDevDiagnostics(opts, env);
    }

    private static void ConfigureSqlServer(DbContextOptionsBuilder opts, string cs, IHostEnvironment env)
    {
        opts.UseSqlServer(cs, mssql =>
            mssql.MigrationsAssembly(typeof(SqlServerDbContext).Assembly.FullName));
        ApplyDevDiagnostics(opts, env);
    }

    private static void ConfigureMySql(DbContextOptionsBuilder opts, string cs, IHostEnvironment env)
    {
        opts.UseMySQL(cs, mysql =>
            mysql.MigrationsAssembly(typeof(MySqlDbContext).Assembly.FullName));
        ApplyDevDiagnostics(opts, env);
    }

    private static void ApplyDevDiagnostics(DbContextOptionsBuilder opts, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            opts.EnableDetailedErrors();
            opts.EnableSensitiveDataLogging();
        }
    }

    /// <summary>
    /// Best-effort: ensure the directory backing a relative SQLite file exists.
    /// SQLite will create the file itself on first connection, but the directory
    /// must exist or <c>OpenAsync</c> throws.
    /// </summary>
    private static void EnsureSqliteDirectory(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
            var dataSource = builder.DataSource;
            if (string.IsNullOrWhiteSpace(dataSource)) return;
            if (dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)) return;

            var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch
        {
            // Non-fatal — connection open will surface a clearer error.
        }
    }

    private static void ConfigureSqliteServices(
        IServiceCollection services, string connectionString, IHostEnvironment environment)
    {
        EnsureSqliteDirectory(connectionString);
        SqliteDapperTypeHandlers.RegisterOnce();
        services.AddDbContext<AppDbContext, SqliteDbContext>(opts =>
            ConfigureSqlite(opts, connectionString, environment));
        services.AddScoped<IDbConnectionFactory, SqliteConnectionFactory>();
    }

    private static void ConfigurePostgresServices(
        IServiceCollection services, string connectionString, IHostEnvironment environment)
    {
        services.AddDbContext<AppDbContext>(opts =>
            ConfigurePostgres(opts, connectionString, environment));
        services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();
    }

    private static void ConfigureSqlServerServices(
        IServiceCollection services, string connectionString, IHostEnvironment environment)
    {
        services.AddDbContext<AppDbContext, SqlServerDbContext>(opts =>
            ConfigureSqlServer(opts, connectionString, environment));
        services.AddScoped<IDbConnectionFactory, SqlServerConnectionFactory>();
    }

    private static void ConfigureMySqlServices(
        IServiceCollection services, string connectionString, IHostEnvironment environment)
    {
        services.AddDbContext<AppDbContext, MySqlDbContext>(opts =>
            ConfigureMySql(opts, connectionString, environment));
        services.AddScoped<IDbConnectionFactory, MySqlConnectionFactory>();
    }
}
