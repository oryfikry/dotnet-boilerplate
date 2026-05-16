using Api.Common.Endpoints;
using Api.Common.Responses;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Features.Products.GetProductById;

public sealed class GetProductByIdEndpoint : IEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/products/{id:guid}", HandleAsync)
           .AddEndpointFilter<ApiResponseEndpointFilter>()
           .WithName("GetProductById")
           .WithTags("Products")
           .Produces<ApiResponse<ProductDto>>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<ProductDto>, NotFound>> HandleAsync(
        Guid id,
        ISender sender,
        CancellationToken ct)
    {
        var dto = await sender.Send(new GetProductByIdQuery(id), ct);
        return dto is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(dto);
    }
}
