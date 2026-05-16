namespace Api.Common.Context;

/// <summary>
/// Per-request ambient context.
///
/// Skeleton placeholder for Milestone 1 — full implementation lands in
/// Milestone 3 along with JWT/permission wiring (PRD v2.1 §4.1) and the
/// cache invalidation pipeline (§4.3).
///
/// All members are nullable in M1 because no auth/cache subsystem is wired
/// yet. The interface lives in <c>Common</c> because both <c>Infrastructure</c>
/// and <c>Features</c> consume it.
/// </summary>
public interface IRequestContext
{
    /// <summary>Authenticated user id, or <see langword="null"/> for anonymous requests.</summary>
    Guid? UserId { get; }

    /// <summary>Tenant identifier (reserved for v3.x — see PRD §9.9).</summary>
    Guid? TenantId { get; }

    /// <summary>Permission claims attached to the current principal.</summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>
    /// Aggregate roots (cache tags) mutated during this request.
    /// Used by query handlers to bypass stale cache (PRD §4.3 read-your-writes).
    /// </summary>
    IReadOnlySet<string> RecentlyMutatedAggregates { get; }

    /// <summary>Mark an aggregate as mutated within the current request scope.</summary>
    void MarkMutated(string aggregateTag);
}

/// <summary>
/// No-op <see cref="IRequestContext"/> used until Milestone 3 wires up a real
/// implementation. Returns empty sets and accepts mutation marks silently so
/// downstream code can be written against the final shape today.
/// </summary>
internal sealed class NullRequestContext : IRequestContext
{
    private readonly HashSet<string> _mutated = new(StringComparer.OrdinalIgnoreCase);

    public Guid? UserId => null;
    public Guid? TenantId => null;
    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
    public IReadOnlySet<string> RecentlyMutatedAggregates => _mutated;

    public void MarkMutated(string aggregateTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateTag);
        _mutated.Add(aggregateTag);
    }
}
