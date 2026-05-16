namespace Api.Infrastructure.Security;

/// <summary>
/// Strongly-typed binding for JWT settings. Bound from the <c>Jwt</c>
/// configuration section.
///
/// PRD v2.1 §4.1 — JWT lifetime ≤ 15 min, refresh token lifetime ≤ 7 days.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 signing key. Must be ≥ 32 bytes (UTF-8). Production keys belong in a secret store.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "dotnet-pbac-boilerplate";

    public string Audience { get; set; } = "dotnet-pbac-boilerplate";

    /// <summary>Access token lifetime; capped at 15 minutes by PRD §4.1.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Refresh token lifetime; capped at 7 days by PRD §4.1.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);
}
