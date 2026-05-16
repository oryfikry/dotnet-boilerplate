using MediatR;

namespace Api.Features.Products.GetProductById;

/// <summary>Query to read a single product by its primary key.</summary>
public sealed record GetProductByIdQuery(Guid Id) : IRequest<ProductDto?>;

/// <summary>Read-side projection of a <see cref="Infrastructure.Data.Entities.Product"/>.</summary>
public sealed record ProductDto(
    Guid Id,
    string Name,
    string Sku,
    decimal Price,
    DateTime CreatedAtUtc);
