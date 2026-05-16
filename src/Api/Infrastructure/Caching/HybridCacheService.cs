using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using StackExchange.Redis;

namespace Api.Infrastructure.Caching;

/// <summary>
/// Hybrid cache implementation per PRD v2.1 §4.2:
///
/// <para>
/// L1 = <see cref="IMemoryCache"/> (per-instance, hot reads). L2 = Redis
/// (shared, source of truth for invalidation). L3 = the caller-supplied
/// factory delegate, which doubles as the fallback path when L2 is
/// unhealthy.
/// </para>
///
/// <para>
/// All L2 operations go through a Polly v8 <see cref="ResiliencePipeline"/>:
/// <c>Timeout → Retry (exponential) → CircuitBreaker</c>. When the pipeline
/// rejects a call (timeout, broken circuit, redis error), we silently fall
/// back to the factory and the request still succeeds.
/// </para>
/// </summary>
internal sealed partial class HybridCacheService : ICacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly IMemoryCache _memory;
    private readonly CacheOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<HybridCacheService> _logger;

    public HybridCacheService(
        IMemoryCache memory,
        IOptions<CacheOptions> options,
        ILogger<HybridCacheService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _memory = memory;
        _options = options.Value;
        _logger = logger;
        _redis = redis;

        _pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(_options.L2Timeout)
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.L2RetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(50),
                ShouldHandle = new PredicateBuilder()
                    .Handle<RedisException>()
                    .Handle<TimeoutRejectedException>()
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = _options.CircuitBreakerFailureRatio,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15),
                ShouldHandle = new PredicateBuilder()
                    .Handle<RedisException>()
                    .Handle<TimeoutRejectedException>()
            })
            .Build();
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan ttl,
        string? tag = null,
        CancellationToken ct = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        var prefixed = Prefixed(key);

        // L1
        if (_memory.TryGetValue(prefixed, out T? hot) && hot is not null)
        {
            return hot;
        }

        // L2 (resilient read)
        if (_redis is not null)
        {
            try
            {
                var raw = await _pipeline.ExecuteAsync(
                    async token => await _redis.GetDatabase().StringGetAsync(prefixed).WaitAsync(token),
                    ct);

                if (raw.HasValue)
                {
                    var bytes = (byte[]?)raw;
                    var value = bytes is null ? null : JsonSerializer.Deserialize<T>(bytes);
                    if (value is not null)
                    {
                        _memory.Set(prefixed, value, ttl);
                    }
                    return value;
                }
            }
            catch (Exception ex) when (IsTransientCacheError(ex))
            {
                LogL2Unavailable(_logger, ex, prefixed);
                return await factory(ct);
            }
        }

        // Miss — load via factory (L3) and try to populate cache.
        var fresh = await factory(ct);
        if (fresh is not null)
        {
            await TryWriteAsync(prefixed, fresh, ttl, tag, ct);
        }
        return fresh;
    }

    public async Task InvalidateAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var prefixed = Prefixed(key);

        _memory.Remove(prefixed);

        if (_redis is null) return;
        try
        {
            await _pipeline.ExecuteAsync(
                async token => await _redis.GetDatabase().KeyDeleteAsync(prefixed).WaitAsync(token),
                ct);
        }
        catch (Exception ex) when (IsTransientCacheError(ex))
        {
            LogL2Unavailable(_logger, ex, prefixed);
        }
    }

    public async Task InvalidateByTagAsync(string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var tagKey = TagKey(tag);

        if (_redis is null)
        {
            // Without L2 we cannot enumerate which L1 keys belong to the tag,
            // so the safest correctness-preserving action is to do nothing in
            // L1 (entries TTL out naturally).
            return;
        }

        try
        {
            var keys = await _pipeline.ExecuteAsync(
                async token => await _redis.GetDatabase().SetMembersAsync(tagKey).WaitAsync(token),
                ct);

            if (keys.Length == 0) return;

            var redisKeys = keys.Select(k => (RedisKey)k.ToString()).ToArray();
            await _pipeline.ExecuteAsync(
                async token => await _redis.GetDatabase().KeyDeleteAsync(redisKeys).WaitAsync(token),
                ct);

            await _pipeline.ExecuteAsync(
                async token => await _redis.GetDatabase().KeyDeleteAsync(tagKey).WaitAsync(token),
                ct);

            foreach (var k in keys)
            {
                _memory.Remove(k.ToString());
            }
        }
        catch (Exception ex) when (IsTransientCacheError(ex))
        {
            LogL2Unavailable(_logger, ex, tagKey);
        }
    }

    private async Task TryWriteAsync<T>(string prefixed, T value, TimeSpan ttl, string? tag, CancellationToken ct)
    {
        _memory.Set(prefixed, value, ttl);

        if (_redis is null) return;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);

            await _pipeline.ExecuteAsync(
                async token => await _redis.GetDatabase().StringSetAsync(prefixed, bytes, ttl).WaitAsync(token),
                ct);

            if (!string.IsNullOrWhiteSpace(tag))
            {
                var tagKey = TagKey(tag);
                await _pipeline.ExecuteAsync(
                    async token => await _redis.GetDatabase().SetAddAsync(tagKey, prefixed).WaitAsync(token),
                    ct);
            }
        }
        catch (Exception ex) when (IsTransientCacheError(ex))
        {
            LogL2Unavailable(_logger, ex, prefixed);
        }
    }

    private string Prefixed(string key) =>
        string.IsNullOrEmpty(_options.KeyPrefix) ? key : $"{_options.KeyPrefix}:{key}";

    private string TagKey(string tag) => Prefixed($"tag:{tag}");

    private static bool IsTransientCacheError(Exception ex) =>
        ex is RedisException
        or BrokenCircuitException
        or TimeoutRejectedException
        or OperationCanceledException;

    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning,
        Message = "Cache L2 unavailable for {Key}; falling back.")]
    private static partial void LogL2Unavailable(ILogger logger, Exception ex, string key);
}
