namespace Api.Common.Caching;

/// <summary>
/// Marks a MediatR <c>IRequest</c> (typically a Command) as invalidating one
/// or more cache <em>tags</em> after the handler completes successfully.
///
/// Per PRD v2.1 §4.3, tag-based invalidation is the canonical pattern:
/// <c>[InvalidatesCache("products")]</c> on a Command tells the
/// <c>CacheInvalidationBehavior</c> pipeline to call
/// <c>ICacheService.InvalidateByTagAsync("products")</c> right after the
/// handler returns. The same behavior also marks the aggregate as
/// "recently mutated" on <c>IRequestContext</c>, enabling read-your-writes
/// for downstream queries within the same request scope.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InvalidatesCacheAttribute(params string[] tags) : Attribute
{
    public string[] Tags { get; } = tags ?? throw new ArgumentNullException(nameof(tags));
}
