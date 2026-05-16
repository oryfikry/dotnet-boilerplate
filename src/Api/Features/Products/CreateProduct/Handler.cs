using Api.Infrastructure.Data;
using Api.Infrastructure.Data.Entities;
using MediatR;

namespace Api.Features.Products.CreateProduct;

internal sealed partial class CreateProductHandler(
    AppDbContext db,
    ILogger<CreateProductHandler> logger)
    : IRequestHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Name = cmd.Name,
            Sku = cmd.Sku,
            Price = cmd.Price
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        LogProductCreated(logger, product.Id, product.Sku);
        return product.Id;
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Product created: id={ProductId} sku={Sku}")]
    private static partial void LogProductCreated(ILogger logger, Guid productId, string sku);
}
