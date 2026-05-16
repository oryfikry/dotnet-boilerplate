using Api.Common.Endpoints;
using Api.Common.Responses;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Features.Auth.RefreshToken;

public sealed class RefreshTokenEndpoint : IEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/refresh", HandleAsync)
           .AddEndpointFilter<ApiResponseEndpointFilter>()
           .AllowAnonymous()
           .RequireRateLimiting("sensitive")
           .WithName("RefreshToken")
           .WithTags("Auth")
           .Produces<ApiResponse<RefreshTokenResponse>>(StatusCodes.Status200OK)
           .ProducesValidationProblem()
           .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Ok<RefreshTokenResponse>> HandleAsync(
        [FromBody] RefreshTokenCommand cmd,
        ISender sender,
        CancellationToken ct)
    {
        var response = await sender.Send(cmd, ct);
        return TypedResults.Ok(response);
    }
}

internal sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }
}
