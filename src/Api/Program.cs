using Api.Common.Behaviors;
using Api.Common.Context;
using Api.Common.Endpoints;
using Api.Common.Exceptions;
using Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------------------------------
// Service registration
// ----------------------------------------------------------------------------

// RFC 7807 ProblemDetails service.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
    };
});

// Global exception handler (PRD §4.4).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// OpenAPI document (built-in for .NET 10, see PRD §3.1).
builder.Services.AddOpenApi();

// Per-request ambient context (M3 will replace NullRequestContext with the
// JWT-aware implementation).
builder.Services.AddScoped<IRequestContext, NullRequestContext>();

// ---- M2: CQRS pipeline ------------------------------------------------------

// MediatR + pipeline behaviors. Order matters: Logging is outermost so it
// records both validation failures and handler exceptions; Validation runs
// before handlers so invalid commands never touch the database.
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

// FluentValidation: discover all validators in the API assembly.
builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);

// ---- M2: Data layer ---------------------------------------------------------

builder.Services
    .AddOptions<DatabaseOptions>()
    .Configure<IConfiguration>((opts, config) =>
        opts.ConnectionString = config.GetConnectionString("Postgres"));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Postgres is not configured. " +
            "Run `docker compose up -d` and set the value in appsettings.Development.json " +
            "or via `dotnet user-secrets set ConnectionStrings:Postgres ...`.");

    options.UseNpgsql(connectionString, npg =>
    {
        npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
    });

    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors();
        options.EnableSensitiveDataLogging();
    }
});

builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

var app = builder.Build();

// ----------------------------------------------------------------------------
// HTTP pipeline
// ----------------------------------------------------------------------------

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Auto-discovery of IEndpoint implementations (PRD §7.1).
// Feature endpoints opt into ApiResponse<T> wrapping by chaining
// .AddEndpointFilter<ApiResponseEndpointFilter>() on their route — see the
// canonical CreateProduct slice (PRD §7.5).
app.MapEndpoints();

app.Run();

// Expose the implicit Program class to the integration test project.
public partial class Program;
