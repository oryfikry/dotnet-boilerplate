using System.Reflection;
using Api.Common.Caching;
using Api.Common.Context;
using Api.Infrastructure.Caching;
using MediatR;

namespace Api.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior that performs cache invalidation after a Command
/// handler completes successfully.
///
/// PRD v2.1 §4.3 — Commands annotated with <see cref="InvalidatesCacheAttribute"/>
/// trigger <c>InvalidateByTagAsync</c> for each tag <em>after</em> the handler
/// returns (so failures don't poison the cache). The aggregate tag is also
/// recorded on <see cref="IRequestContext.RecentlyMutatedAggregates"/> so
/// subsequent queries within the same request can bypass stale reads.
/// </summary>
internal sealed partial class CacheInvalidationBehavior<TRequest, TResponse>(
    ICacheService cache,
    IRequestContext requestContext,
    ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly InvalidatesCacheAttribute? Attribute =
        typeof(TRequest).GetCustomAttribute<InvalidatesCacheAttribute>();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (Attribute is null || Attribute.Tags.Length == 0)
        {
            return response;
        }

        foreach (var tag in Attribute.Tags)
        {
            requestContext.MarkMutated(tag);
            try
            {
                await cache.InvalidateByTagAsync(tag, cancellationToken);
            }
#pragma warning disable CA1031 // Best-effort cache invalidation must not fail the command.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogInvalidationFailed(logger, ex, tag);
            }
        }

        return response;
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Warning,
        Message = "Cache invalidation for tag '{Tag}' failed; mutation succeeded but readers may see stale data.")]
    private static partial void LogInvalidationFailed(ILogger logger, Exception ex, string tag);
}
