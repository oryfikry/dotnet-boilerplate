using System.Net;
using System.Net.Http.Json;
using Api.Common.Responses;
using Api.Features.Products.CreateProduct;
using Api.Features.Products.GetProductById;
using Api.IntegrationTests.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Api.IntegrationTests.Features.Products.GetProductById;

public sealed class GetProductByIdTests : BaseIntegrationTest
{
    public GetProductByIdTests(IntegrationTestWebAppFactory factory) : base(factory) { }

    [Fact]
    public async Task GET_with_permission_returns_200_and_dto()
    {
        var user = await SeedUserAsync(
            email: "reader@local",
            permissions: ["products.create", "products.read"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        var id = await Sender.Send(new CreateProductCommand("Espresso", "SKU-ESP", 12.5m));

        var response = await client.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<ProductDto>>(JsonOpts);
        envelope.ShouldNotBeNull();
        envelope!.Data.ShouldNotBeNull();
        envelope.Data!.Id.ShouldBe(id);
        envelope.Data.Sku.ShouldBe("SKU-ESP");
        envelope.Data.Price.ShouldBe(12.5m);
    }

    [Fact]
    public async Task GET_unknown_id_returns_404()
    {
        var user = await SeedUserAsync(email: "reader2@local", permissions: ["products.read"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        var response = await client.GetAsync(new Uri($"/api/v1/products/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_caches_subsequent_reads_via_L1()
    {
        var user = await SeedUserAsync(
            email: "cacher@local",
            permissions: ["products.create", "products.read"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        // Insert via raw SQL so we don't trigger the cache-invalidation /
        // read-your-writes plumbing — pretend the row was always there.
        var id = Guid.CreateVersion7();
        await using (var raw = await Services.GetRequiredService<Api.Infrastructure.Data.IDbConnectionFactory>()
            .CreateOpenConnectionAsync())
        {
            using var cmd = raw.CreateCommand();
            cmd.CommandText = """
                INSERT INTO products
                  (id, name, sku, price, created_at_utc, is_deleted)
                VALUES
                  (@id, 'Latte', 'SKU-LAT', 7.5, NOW() AT TIME ZONE 'UTC', FALSE);
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "id";
            p.Value = id;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync();
        }

        // First HTTP read populates L1 (no Redis in tests — see WebAppFactory).
        var first = await client.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<ApiResponse<ProductDto>>(JsonOpts);
        firstBody!.Data!.Price.ShouldBe(7.5m);

        // Mutate the row outside the cache pipeline.
        await using (var raw = await Services.GetRequiredService<Api.Infrastructure.Data.IDbConnectionFactory>()
            .CreateOpenConnectionAsync())
        {
            using var cmd = raw.CreateCommand();
            cmd.CommandText = "UPDATE products SET price = 999 WHERE id = @id";
            var p = cmd.CreateParameter();
            p.ParameterName = "id";
            p.Value = id;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync();
        }

        // Second HTTP read still returns the cached value because nothing
        // invalidated the tag.
        var second = await client.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative));
        var secondBody = await second.Content.ReadFromJsonAsync<ApiResponse<ProductDto>>(JsonOpts);
        secondBody!.Data!.Price.ShouldBe(7.5m,
            "second read should hit L1 cache, not the mutated DB row");
    }

    [Fact]
    public async Task Create_then_Get_in_same_request_scope_reads_through_cache_for_freshness()
    {
        // Read-your-writes (PRD §4.3): when a Command on aggregate "products"
        // succeeds within the same request scope (via MediatR pipeline +
        // CacheInvalidationBehavior), a follow-up Query in that scope MUST
        // bypass the cache. We exercise this directly via ISender so both
        // calls share IRequestContext — the same condition the production
        // pipeline guarantees per HTTP request.
        var user = await SeedUserAsync(
            email: "ryw@local",
            permissions: ["products.create", "products.read"]);

        var id = await Sender.Send(new CreateProductCommand("Mocha", "SKU-MOC", 5m));

        var dto = await Sender.Send(new GetProductByIdQuery(id));

        dto.ShouldNotBeNull();
        dto!.Price.ShouldBe(5m);
    }

    [Fact]
    public async Task GET_anonymous_returns_401()
    {
        var client = CreateClient();
        var response = await client.GetAsync(new Uri($"/api/v1/products/{Guid.NewGuid()}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_authenticated_without_permission_returns_403()
    {
        var user = await SeedUserAsync(email: "noperm-r@local", permissions: ["products.create"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        var response = await client.GetAsync(new Uri($"/api/v1/products/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
