using Api.Common.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Features.System;

/// <summary>
/// Built-in system endpoints — liveness probe and root info.
///
/// <para>
/// PRD v2.1 §9.5 mandates three health-check endpoints (live, ready, startup).
/// Milestone 1 ships only the liveness probe; readiness/startup will be added
/// in Milestone 5 once Postgres/Redis dependencies are introduced.
/// </para>
/// </summary>
public sealed class SystemEndpoints : IEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => TypedResults.Ok(new HealthStatus("Healthy", DateTimeOffset.UtcNow)))
           .WithName("HealthLive")
           .WithTags("System")
           .AllowAnonymous();

        app.MapGet("/api/v1", () => TypedResults.Ok(new ApiInfo(
                Name: "dotnet-pbac-boilerplate",
                Version: typeof(SystemEndpoints).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                Environment: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")))
           .WithName("ApiInfo")
           .WithTags("System")
           .AllowAnonymous();
    }

    private sealed record HealthStatus(string Status, DateTimeOffset Timestamp);

    private sealed record ApiInfo(string Name, string Version, string Environment);
}
