using Api.Common.Context;
using Api.Common.Endpoints;
using Api.Common.Exceptions;

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

// Per-request ambient context. NullRequestContext is the M1 placeholder
// (see Common/Context/IRequestContext.cs); replaced in Milestone 3.
builder.Services.AddScoped<IRequestContext, NullRequestContext>();

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
app.MapEndpoints();

app.Run();

// Expose the implicit Program class to the integration test project.
public partial class Program;
