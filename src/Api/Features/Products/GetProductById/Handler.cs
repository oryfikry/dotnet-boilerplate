using Api.Infrastructure.Data;
using Dapper;
using MediatR;

namespace Api.Features.Products.GetProductById;

/// <summary>
/// Dapper-backed query handler. PRD v2.1 §6 directive #3 — reads use
/// <see cref="IDbConnectionFactory"/>, not <c>AppDbContext</c>.
/// </summary>
internal sealed class GetProductByIdHandler(IDbConnectionFactory connectionFactory)
    : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private const string Sql = """
        SELECT  id              AS Id,
                name            AS Name,
                sku             AS Sku,
                price           AS Price,
                created_at_utc  AS CreatedAtUtc
        FROM    products
        WHERE   id = @Id
          AND   is_deleted = FALSE;
        """;

    public async Task<ProductDto?> Handle(GetProductByIdQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        var command = new CommandDefinition(Sql, new { query.Id }, cancellationToken: ct);

        return await connection.QuerySingleOrDefaultAsync<ProductDto>(command);
    }
}
