using MediatR;

namespace Api.Features.Products.CreateProduct;

/// <summary>
/// Command to create a new <see cref="Infrastructure.Data.Entities.Product"/>.
/// PRD v2.1 §7.2 — canonical slice template.
/// </summary>
public sealed record CreateProductCommand(
    string Name,
    string Sku,
    decimal Price) : IRequest<Guid>;
