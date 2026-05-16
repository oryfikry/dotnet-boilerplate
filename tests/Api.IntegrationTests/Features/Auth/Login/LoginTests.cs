using System.Net;
using System.Net.Http.Json;
using Api.Common.Responses;
using Api.Features.Auth.Login;
using Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Api.IntegrationTests.Features.Auth.Login;

public sealed class LoginTests : BaseIntegrationTest
{
    public LoginTests(IntegrationTestWebAppFactory factory) : base(factory) { }

    [Fact]
    public async Task POST_with_valid_credentials_returns_200_and_token_pair()
    {
        var user = await SeedUserAsync(
            email: "login-ok@local",
            password: "Test123!Test123!",
            permissions: ["products.read"]);

        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand("login-ok@local", "Test123!Test123!"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOpts);
        envelope.ShouldNotBeNull();
        envelope!.Data.ShouldNotBeNull();
        envelope.Data!.AccessToken.ShouldNotBeNullOrEmpty();
        envelope.Data.RefreshToken.ShouldNotBeNullOrEmpty();
        envelope.Data.AccessTokenExpiresAtUtc.ShouldBeGreaterThan(DateTime.UtcNow);

        // A refresh token row must have been persisted with a hashed value (not the raw).
        var persisted = await Db.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.UserId == user.Id);
        persisted.ShouldNotBeNull();
        persisted!.TokenHash.ShouldNotBe(envelope.Data.RefreshToken);
        persisted.RevokedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task POST_with_wrong_password_returns_401()
    {
        await SeedUserAsync(email: "login-bad@local", password: "Test123!Test123!");
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand("login-bad@local", "wrong-password"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_with_unknown_email_returns_401()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand("nobody@local", "anything"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_with_inactive_user_returns_401()
    {
        await SeedUserAsync(
            email: "inactive@local",
            password: "Test123!Test123!",
            isActive: false);

        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand("inactive@local", "Test123!Test123!"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_email_is_case_insensitive()
    {
        await SeedUserAsync(email: "case@local", password: "Test123!Test123!");
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginCommand("CASE@Local", "Test123!Test123!"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
