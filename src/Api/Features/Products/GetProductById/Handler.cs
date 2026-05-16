using Api.Common.Context;
using Api.Infrastructure.Caching;
using Api.Infrastructure.Data;
using Dapper;
using MediatR;

namespace Api.Features.Products.GetProductById;

/// <summary>
/// Dapper-backed query handler with hybrid cache (PRD v2.1 §4.2-§4.3).
///
/// On read: L1 → L2 (Redis) → L3 (Dapper SQL). Tag <c>products</c> is invalidated
/// by <c>CreateProductCommand</c> via <c>[InvalidatesCache("products")]</c>.
/// Reads bypass cache for the current request scope if a Command on the same
/// aggregate has just succeeded (read-your-writes).
/// </summary>
internal sealed class GetProductByIdHandler(
    IDbConnectionFactory connectionFactory,
    ICacheService cache,
    IRequestContext requestContext)
    : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private const string AggregateTag = "products";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private const string Sql = """
        SELECT  id              AS Id,
                name            AS Name,
                sku             AS Sku,
                price           AS Price,
                created_at_utc  AS CreatedAtUtc
        FROM    products
        WHERE   id = @Id
          AND   is_deleted = @IsDeleted;
        """;

    public async Task<ProductDto?> Handle(GetProductByIdQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Read-your-writes — if the same aggregate was just mutated within this
        // request scope, bypass the cache to guarantee freshness (PRD §4.3).
        if (requestContext.RecentlyMutatedAggregates.Contains(AggregateTag))
        {
            return await LoadFromDbAsync(query.Id, ct);
        }

        return await cache.GetOrSetAsync(
            $"products:{query.Id}",
            token => LoadFromDbAsync(query.Id, token),
            CacheTtl,
            tag: AggregateTag,
            ct: ct);
    }

    private async Task<ProductDto?> LoadFromDbAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        // Parameterized predicate so the bool literal is provider-portable
        // (PostgreSQL: TRUE/FALSE, SQLite/SQL Server/MySQL: 1/0). Each ADO
        // mapper handles the cast natively. ADR-0001.
        // ⚠️ DO NOT use literal TRUE/FALSE in SQL — breaks SQLite/MySQL.
        var command = new CommandDefinition(Sql, new { Id = id, IsDeleted = false }, cancellationToken: ct);
        return await connection.QuerySingleOrDefaultAsync<ProductDto>(command);
    }
}
