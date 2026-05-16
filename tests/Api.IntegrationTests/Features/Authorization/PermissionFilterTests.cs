using System.Net;
using System.Net.Http.Json;
using Api.Features.Products.CreateProduct;
using Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Api.IntegrationTests.Features.Authorization;

/// <summary>
/// Targeted coverage of <see cref="Api.Infrastructure.Security.RequirePermissionFilter"/>
/// behavior across the auth/permission triangle:
///   anonymous       → 401
///   authed, no perm → 403
///   authed, granted → 2xx (delegates to handler)
/// PRD v2.2 §4.1.
/// </summary>
public sealed class PermissionFilterTests : BaseIntegrationTest
{
    public PermissionFilterTests(IntegrationTestWebAppFactory factory) : base(factory) { }

    [Fact]
    public async Task Anonymous_request_to_protected_endpoint_returns_401()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand("X", "SKU-A", 1m));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_without_permission_returns_403()
    {
        var user = await SeedUserAsync(
            email: "perm-403@local",
            permissions: ["products.read"]); // wrong perm for create
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand("X", "SKU-A", 1m));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authenticated_with_granted_permission_passes_filter()
    {
        var user = await SeedUserAsync(
            email: "perm-200@local",
            permissions: ["products.create"]);
        var token = IssueAccessToken(user);
        var client = CreateClient(token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand("X", "SKU-OK", 1m));

        // Created<T> is unwrapped to Ok by ApiResponseEndpointFilter (M3 known issue).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bearer_token_with_invalid_signature_returns_401()
    {
        var client = CreateClient(bearerToken: "not.a.valid.jwt");
        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductCommand("X", "SKU-A", 1m));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
