using Api.Common.Endpoints;
using Api.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Features.Products.CreateProduct;

/// <summary>
/// Canonical create-product endpoint (PRD v2.1 §7.5).
///
/// Note: <c>.RequirePermission()</c> and <c>.RequireIdempotency()</c> are
/// reserved for Milestone 3. They are documented in the PRD and will be
/// applied here once those subsystems are wired in.
/// </summary>
public sealed class CreateProductEndpoint : IEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/products", HandleAsync)
           .AddEndpointFilter<ApiResponseEndpointFilter>()
           .WithName("CreateProduct")
           .WithTags("Products")
           .Produces<ApiResponse<Guid>>(StatusCodes.Status201Created)
           .ProducesValidationProblem();
    }

    private static async Task<Created<Guid>> HandleAsync(
        [FromBody] CreateProductCommand cmd,
        ISender sender,
        CancellationToken ct)
    {
        var id = await sender.Send(cmd, ct);
        return TypedResults.Created($"/api/v1/products/{id}", id);
    }
}
