using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Infrastructure.Security;

/// <summary>
/// Issues access (JWT) and refresh tokens.
///
/// PRD v2.1 §4.1 — permissions are flattened as repeated <c>permissions</c>
/// claims in the JWT (snapshot at login time, not live-looked-up).
/// </summary>
public interface IJwtTokenService
{
    AccessTokenResult IssueAccessToken(Guid userId, IReadOnlyCollection<string> permissions);

    /// <summary>
    /// Generate a fresh refresh token. The raw value is returned to the
    /// caller (and to the client); only the SHA-256 hash should ever be
    /// persisted (PRD §4.1).
    /// </summary>
    RefreshTokenResult IssueRefreshToken();

    /// <summary>SHA-256 hash a refresh token for lookup against <c>refresh_tokens.token_hash</c>.</summary>
    string HashRefreshToken(string rawToken);
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public sealed record RefreshTokenResult(string RawToken, string TokenHash, DateTime ExpiresAtUtc);

internal sealed class JwtTokenService(
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IJwtTokenService
{
    private const int RefreshTokenSizeBytes = 32;
    private readonly JwtOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly JwtSecurityTokenHandler _handler = new();

    public AccessTokenResult IssueAccessToken(Guid userId, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (string.IsNullOrEmpty(_options.SigningKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = nowUtc.Add(_options.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(nowUtc).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        foreach (var p in permissions)
        {
            claims.Add(new Claim("permissions", p));
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (UTF-8) for HS256.");
        }

        var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: nowUtc,
            expires: expiresAtUtc,
            signingCredentials: creds);

        return new AccessTokenResult(_handler.WriteToken(token), expiresAtUtc);
    }

    public RefreshTokenResult IssueRefreshToken()
    {
        var raw = RandomNumberGenerator.GetBytes(RefreshTokenSizeBytes);
        var rawBase64 = Convert.ToBase64String(raw);
        var hash = Sha256(rawBase64);
        var expiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime.Add(_options.RefreshTokenLifetime);
        return new RefreshTokenResult(rawBase64, hash, expiresAtUtc);
    }

    public string HashRefreshToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawToken);
        return Sha256(rawToken);
    }

    private static string Sha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
