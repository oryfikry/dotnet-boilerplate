using Api.Common.Endpoints;
using Api.Common.Responses;
using Api.Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Api.Features.Products.CreateProduct;

public sealed class CreateProductEndpoint : IEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/products", HandleAsync)
           .RequirePermission("products.create")
           .AddEndpointFilter<ApiResponseEndpointFilter>()
           .WithName("CreateProduct")
           .WithTags("Products")
           .Produces<ApiResponse<Guid>>(StatusCodes.Status201Created)
           .ProducesValidationProblem()
           .ProducesProblem(StatusCodes.Status401Unauthorized)
           .ProducesProblem(StatusCodes.Status403Forbidden);
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
