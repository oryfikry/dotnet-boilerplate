using Api.Infrastructure.Data;
using Api.Infrastructure.Data.Entities;
using Api.Infrastructure.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Auth.RefreshToken;

internal sealed partial class RefreshTokenHandler(
    AppDbContext db,
    IJwtTokenService tokens,
    TimeProvider timeProvider,
    ILogger<RefreshTokenHandler> logger)
    : IRequestHandler<RefreshTokenCommand, RefreshTokenResponse>
{
    public async Task<RefreshTokenResponse> Handle(RefreshTokenCommand cmd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var hash = tokens.HashRefreshToken(cmd.RefreshToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await db.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u!.UserPermissions)
                    .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);

        if (existing is null
            || existing.RevokedAtUtc is not null
            || existing.ExpiresAtUtc <= nowUtc
            || existing.User is null
            || !existing.User.IsActive)
        {
            LogRefreshRejected(logger);
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");
        }

        var permissions = existing.User.UserPermissions
            .Where(up => up.Permission is not null)
            .Select(up => up.Permission!.Name)
            .ToArray();

        var newAccess = tokens.IssueAccessToken(existing.User.Id, permissions);
        var newRefresh = tokens.IssueRefreshToken();

        var newEntity = new Api.Infrastructure.Data.Entities.RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = existing.User.Id,
            TokenHash = newRefresh.TokenHash,
            ExpiresAtUtc = newRefresh.ExpiresAtUtc
        };
        db.RefreshTokens.Add(newEntity);

        existing.RevokedAtUtc = nowUtc;
        existing.ReplacedByTokenId = newEntity.Id;

        await db.SaveChangesAsync(ct);

        LogRefreshSucceeded(logger, existing.User.Id);

        return new RefreshTokenResponse(
            newAccess.Token,
            newAccess.ExpiresAtUtc,
            newRefresh.RawToken,
            newRefresh.ExpiresAtUtc);
    }

    [LoggerMessage(EventId = 6100, Level = LogLevel.Warning, Message = "Refresh token rejected.")]
    private static partial void LogRefreshRejected(ILogger logger);

    [LoggerMessage(EventId = 6101, Level = LogLevel.Information, Message = "Refresh token rotated for user {UserId}")]
    private static partial void LogRefreshSucceeded(ILogger logger, Guid userId);
}
