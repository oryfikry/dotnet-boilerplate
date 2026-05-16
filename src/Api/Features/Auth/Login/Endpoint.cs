using Api.Common.Endpoints;
using Api.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Features.Auth.Login;

public sealed class LoginEndpoint : IEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/login", HandleAsync)
           .AddEndpointFilter<ApiResponseEndpointFilter>()
           .AllowAnonymous()
           .RequireRateLimiting("sensitive")
           .WithName("Login")
           .WithTags("Auth")
           .Produces<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)
           .ProducesValidationProblem()
           .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Ok<LoginResponse>> HandleAsync(
        [FromBody] LoginCommand cmd,
        ISender sender,
        CancellationToken ct)
    {
        var response = await sender.Send(cmd, ct);
        return TypedResults.Ok(response);
    }
}
