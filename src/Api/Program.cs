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

// ---- M2: Data layer (multi-provider, ADR-0001) ------------------------------
builder.Services.AddAppDatabase(builder.Configuration, builder.Environment);

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
// Tests opt out via "RateLimiting:Enabled=false" so login/refresh suites can
// run in tight loops without hitting the 10/min "sensitive" policy. Production
// and Development always have rate limiting on.
var rateLimitingEnabled = builder.Configuration.GetValue("RateLimiting:Enabled", true);
if (rateLimitingEnabled)
{
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
}
else
{
    // No-op limiter so endpoints with .RequireRateLimiting("sensitive") still
    // resolve a policy in the pipeline (otherwise startup throws). The "sensitive"
    // policy here grants effectively unlimited permits.
    builder.Services.AddRateLimiter(opts =>
    {
        opts.AddPolicy("sensitive", _ => RateLimitPartition.GetNoLimiter("noop"));
    });
}

var app = builder.Build();

// ----------------------------------------------------------------------------
// HTTP pipeline
// ----------------------------------------------------------------------------

app.UseExceptionHandler();
app.UseStatusCodePages(context =>
{
    // Return empty body for 404s (and other status codes without response body).
    // Exceptions still go through ExceptionHandler middleware above.
    if (context.HttpContext.Response.StatusCode == 404 && !context.HttpContext.Response.HasStarted)
    {
        context.HttpContext.Response.ContentType = "text/plain";
        context.HttpContext.Response.ContentLength = 0;
    }
    return Task.CompletedTask;
});
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
