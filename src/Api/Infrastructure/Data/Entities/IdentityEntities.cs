namespace Api.Infrastructure.Data.Entities;

/// <summary>
/// Authentication principal. Skeleton — full identity wiring lands in
/// Milestone 3 (PRD v2.1 §4.1). Included now so the initial migration
/// covers the full v1 schema and avoids churn at the M3 boundary.
/// </summary>
public sealed class User : SoftDeletableEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<UserPermission> UserPermissions { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

/// <summary>
/// Granular permission catalog (e.g. <c>products.create</c>).
/// PRD v2.1 §4.1 — permissions are stored, not roles.
/// </summary>
public sealed class Permission : AuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>Stable string identifier such as <c>products.create</c>.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ICollection<UserPermission> UserPermissions { get; set; } = [];
}

/// <summary>Many-to-many join entity between <see cref="User"/> and <see cref="Permission"/>.</summary>
public sealed class UserPermission : AuditableEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

/// <summary>
/// Hashed refresh token row. PRD v2.1 §4.1 — JWT lifetime ≤ 15 min,
/// refresh token lifetime ≤ 7 days, rotated on each use.
/// </summary>
public sealed class RefreshToken : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256 hash of the actual refresh token. Never stored in plain text.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>If this token was rotated, the id of the replacement.</summary>
    public Guid? ReplacedByTokenId { get; set; }
}

/// <summary>
/// Idempotency record for command endpoints. PRD v2.1 §9.2 — TTL 24h,
/// conflict (same key, different request hash) returns 409.
/// </summary>
public sealed class IdempotencyKey
{
    /// <summary>Client-supplied <c>Idempotency-Key</c> header.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>SHA-256 of the request body to detect replays vs conflicts.</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Status code of the original response.</summary>
    public int StatusCode { get; set; }

    /// <summary>Serialized response body for replay.</summary>
    public string? ResponseBody { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
