using System.Net;
using System.Net.Http.Json;
using Api.Common.Responses;
using Api.Features.Auth.RefreshToken;
using Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Api.IntegrationTests.Features.Auth.RefreshToken;

public sealed class RefreshTokenTests : BaseIntegrationTest
{
    public RefreshTokenTests(IntegrationTestWebAppFactory factory) : base(factory) { }

    [Fact]
    public async Task POST_rotates_token_and_revokes_previous()
    {
        var user = await SeedUserAsync(
            email: "rot@local",
            password: "Test123!Test123!",
            permissions: ["products.read"]);
        var login = await LoginHttpAsync("rot@local", "Test123!Test123!");

        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenCommand(login.RefreshToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<RefreshTokenResponse>>(JsonOpts);
        envelope.ShouldNotBeNull();
        envelope!.Data.ShouldNotBeNull();
        envelope.Data!.RefreshToken.ShouldNotBe(login.RefreshToken);
        envelope.Data.AccessToken.ShouldNotBeNullOrEmpty();

        // Old token should be marked revoked + linked to the new one.
        var refreshTokens = await Db.RefreshTokens.AsNoTracking()
            .Where(rt => rt.UserId == user.Id)
            .ToListAsync();
        refreshTokens.Count.ShouldBe(2);
        var oldRow = refreshTokens.Single(rt => rt.RevokedAtUtc != null);
        oldRow.ReplacedByTokenId.ShouldNotBeNull();
    }

    [Fact]
    public async Task POST_with_revoked_token_returns_401()
    {
        await SeedUserAsync(
            email: "reuse@local",
            password: "Test123!Test123!",
            permissions: ["products.read"]);
        var login = await LoginHttpAsync("reuse@local", "Test123!Test123!");

        var client = CreateClient();
        var first = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenCommand(login.RefreshToken));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenCommand(login.RefreshToken));

        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_with_unknown_token_returns_401()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenCommand("totally-bogus-refresh-token"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_with_empty_token_returns_400()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenCommand(""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
