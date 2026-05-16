namespace Api.Infrastructure.Caching;

/// <summary>
/// Cache abstraction with built-in resilience and L1/L2/L3 fallback.
///
/// Per PRD v2.1 §4.2, callers never see Redis exceptions — the implementation
/// wraps L2 access in a Polly pipeline (Timeout → Retry → CircuitBreaker)
/// and degrades to the supplied <paramref name="factory"/> on failure.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Get a value from cache; on miss, invoke <paramref name="factory"/> and
    /// store the result. The same <paramref name="factory"/> is also the L3
    /// fallback if the L2 cache is unhealthy.
    /// </summary>
    /// <param name="key">Fully qualified cache key (callers are responsible for namespacing).</param>
    /// <param name="factory">Async function that produces the value on miss / fallback.</param>
    /// <param name="ttl">Absolute expiration relative to write time.</param>
    /// <param name="tag">Optional tag for tag-based invalidation (PRD §4.3).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan ttl,
        string? tag = null,
        CancellationToken ct = default) where T : class;

    /// <summary>Remove a single key from L1 + L2.</summary>
    Task InvalidateAsync(string key, CancellationToken ct = default);

    /// <summary>Remove every key associated with the given tag from L1 + L2.</summary>
    Task InvalidateByTagAsync(string tag, CancellationToken ct = default);
}

/// <summary>
/// Strongly-typed binding for cache settings. Bound from the <c>Cache</c>
/// configuration section.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Redis connection string (e.g. <c>localhost:6379</c>).</summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>Prefix prepended to every cache key for namespacing.</summary>
    public string KeyPrefix { get; set; } = "pbac";

    /// <summary>Default TTL when a caller does not specify one.</summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Polly timeout per L2 operation (PRD §4.2).</summary>
    public TimeSpan L2Timeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Polly retry attempts on transient L2 failures.</summary>
    public int L2RetryAttempts { get; set; } = 2;

    /// <summary>Polly circuit-breaker failure ratio (0–1) before tripping.</summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;
}
