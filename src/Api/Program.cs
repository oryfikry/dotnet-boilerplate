using System.Text;
using System.Threading.RateLimiting;
using Api.Common.Behaviors;
using Api.Common.Context;
using Api.Common.Endpoints;
using Api.Common.Exceptions;
using Api.Infrastructure.Caching;
using Api.Infrastructure.Data;
using Api.Infrastructure.Data.Seeders;
using Api.Infrastructure.Security;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------------------------------
// Service registration
// ----------------------------------------------------------------------------

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);

// IRequestContext: HttpContext-aware (PRD §4.1, §9.6).
builder.Services.AddScoped<IRequestContext, HttpContextRequestContext>();

// ---- M2: CQRS pipeline ------------------------------------------------------
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(CacheInvalidationBehavior<,>));
});

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
        npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors();
        options.EnableSensitiveDataLogging();
    }
});

builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();

// ---- M3: Caching (PRD §4.2) -------------------------------------------------
builder.Services
    .AddOptions<CacheOptions>()
    .Bind(builder.Configuration.GetSection(CacheOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddMemoryCache();

var redisConnectionString = builder.Configuration["Cache:RedisConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(redisConnectionString));
}

builder.Services.AddSingleton<ICacheService, HybridCacheService>();

// ---- M3: Security (PRD §4.1) ------------------------------------------------
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrEmpty(o.SigningKey)
                   && Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
        "Jwt:SigningKey must be at least 32 bytes (UTF-8).")
    .ValidateOnStart();

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                  ?? new JwtOptions();
        var keyBytes = Encoding.UTF8.GetBytes(
            string.IsNullOrEmpty(jwt.SigningKey)
                ? new string('x', 32)            // placeholder; .ValidateOnStart catches the real misconfig.
                : jwt.SigningKey);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// ---- M3: Rate limiting (PRD §9.4) ------------------------------------------
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Default global policy: 100 req/min per IP, fixed window.
    opts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    // Sensitive policy (login / refresh): 10 req/min per IP.
    opts.AddPolicy("sensitive", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

var app = builder.Build();

// ----------------------------------------------------------------------------
// HTTP pipeline
// ----------------------------------------------------------------------------

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapEndpoints();

// Dev seeder (PRD §11 M3 closing checklist).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DevSeeder.SeedAsync(db, hasher);
}

app.Run();

public partial class Program;
