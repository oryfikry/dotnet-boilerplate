using Api.Infrastructure.Data;
using Api.Infrastructure.Data.Entities;
using Api.Infrastructure.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Auth.Login;

internal sealed partial class LoginHandler(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IJwtTokenService tokens,
    ILogger<LoginHandler> logger)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(LoginCommand cmd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var email = cmd.Email.Trim().ToLowerInvariant();

        var user = await db.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);

        if (user is null || !passwordHasher.Verify(cmd.Password, user.PasswordHash))
        {
            // Same error for both cases — never leak which side mismatched.
            LogLoginFailed(logger, email);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        var permissions = user.UserPermissions
            .Where(up => up.Permission is not null)
            .Select(up => up.Permission!.Name)
            .ToArray();

        var access = tokens.IssueAccessToken(user.Id, permissions);
        var refresh = tokens.IssueRefreshToken();

        db.RefreshTokens.Add(new Api.Infrastructure.Data.Entities.RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            ExpiresAtUtc = refresh.ExpiresAtUtc
        });
        await db.SaveChangesAsync(ct);

        LogLoginSucceeded(logger, user.Id);

        return new LoginResponse(
            access.Token,
            access.ExpiresAtUtc,
            refresh.RawToken,
            refresh.ExpiresAtUtc);
    }

    [LoggerMessage(EventId = 6000, Level = LogLevel.Warning,
        Message = "Login failed for {Email}")]
    private static partial void LogLoginFailed(ILogger logger, string email);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information,
        Message = "Login succeeded for user {UserId}")]
    private static partial void LogLoginSucceeded(ILogger logger, Guid userId);
}
