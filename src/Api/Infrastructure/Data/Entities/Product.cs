namespace Api.Infrastructure.Data.Entities;

/// <summary>
/// Demo aggregate root used by the canonical slice example
/// (PRD v2.1 §7 — <c>CreateProduct</c> / <c>GetProductById</c>).
/// </summary>
public sealed class Product : SoftDeletableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
