using System.Net;
using System.Net.Http.Json;
using Api.Common.Responses;
using Api.Features.Products.CreateProduct;
using Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Api.IntegrationTests.Features.Products.CreateProduct;

public sealed class CreateProductTests : BaseIntegrationTest
{
    public CreateProductTests(IntegrationTestWebAppFactory factory) : base(factory) { }

    [Fact]
    public async Task POST_with_valid_payload_and_permission_returns_200_and_persists()
    {
        // Arrange
        var user = await SeedUserAsync(
            email: "create@local",
            permissions: ["products.create"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand(Name: "Coffee", Sku: "SKU-001", Price: 9.99m));

        // Assert — Created<T> currently re-wraps to Ok via ApiResponseEndpointFilter
        // (PRD v2.2 §M3 known limitation). Tests assert the actual wire shape.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<Guid>>(JsonOpts);
        envelope.ShouldNotBeNull();
        envelope!.Success.ShouldBeTrue();
        envelope.Data.ShouldNotBe(Guid.Empty);

        var persisted = await Db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == envelope.Data);
        persisted.ShouldNotBeNull();
        persisted!.Name.ShouldBe("Coffee");
        persisted.Sku.ShouldBe("SKU-001");
        persisted.Price.ShouldBe(9.99m);
        persisted.IsDeleted.ShouldBeFalse();
        persisted.CreatedBy.ShouldBe(user.Id);
    }

    [Fact]
    public async Task POST_with_invalid_sku_returns_400_validation_problem()
    {
        var user = await SeedUserAsync(email: "validate@local", permissions: ["products.create"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand(Name: "X", Sku: "lowercase!", Price: 1m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Sku");
    }

    [Fact]
    public async Task POST_anonymous_returns_401()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand(Name: "X", Sku: "SKU-A", Price: 1m));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_authenticated_without_permission_returns_403()
    {
        var user = await SeedUserAsync(email: "noperm@local", permissions: ["products.read"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand(Name: "X", Sku: "SKU-A", Price: 1m));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task POST_negative_price_returns_400()
    {
        var user = await SeedUserAsync(email: "neg@local", permissions: ["products.create"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand(Name: "X", Sku: "SKU-A", Price: -1m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
