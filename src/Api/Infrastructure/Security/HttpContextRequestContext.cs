using System.Security.Claims;
using Api.Common.Context;

namespace Api.Infrastructure.Security;

/// <summary>
/// <see cref="IRequestContext"/> implementation that pulls identity data from
/// the current <see cref="HttpContext"/> via <see cref="IHttpContextAccessor"/>.
///
/// PRD v2.1 §4.1 — JWT permissions are attached as repeated <c>permissions</c>
/// claims; the <c>sub</c> claim carries the user id. This implementation also
/// tracks aggregates mutated within the current request scope to support
/// read-your-writes (PRD §4.3).
/// </summary>
internal sealed class HttpContextRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    private readonly HashSet<string> _mutated = new(StringComparer.OrdinalIgnoreCase);

    public Guid? UserId
    {
        get
        {
            var sub = accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? accessor.HttpContext?.User?.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public Guid? TenantId
    {
        get
        {
            // Reserved for v3.x (PRD §9.9). Always null in v2.1.
            var tenant = accessor.HttpContext?.User?.FindFirstValue("tenant");
            return Guid.TryParse(tenant, out var id) ? id : null;
        }
    }

    public IReadOnlySet<string> Permissions
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user is null) return EmptyPermissions;

            var values = user.FindAll("permissions").Select(c => c.Value);
            return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlySet<string> RecentlyMutatedAggregates => _mutated;

    public void MarkMutated(string aggregateTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateTag);
        _mutated.Add(aggregateTag);
    }

    private static readonly IReadOnlySet<string> EmptyPermissions = new HashSet<string>();
}
