using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ruya.Diagnostics.DistributedTracing;

public sealed class DistributedTracingService : IDistributedTracing, IDisposable
{
	private readonly ILogger<DistributedTracingService> _logger;
	private readonly ActivitySource _activitySource;
	private readonly Meter _meter;
	private readonly DistributedTracingSettings _settings;
	private readonly IDistributedCache _distributedCache;
    private readonly UpDownCounter<int> _activeActivitiesCounter;
    private readonly Counter<long> _activitiesCreatedCounter;
    private readonly Counter<long> _cacheMissCounter;
    private readonly Histogram<double> _activityDurationHistogram;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _activeActivityIds = new();

	public DistributedTracingService(
	ILogger<DistributedTracingService> logger,
	ActivitySource activitySource,
	IMeterFactory meterFactory,
	IOptions<DistributedTracingSettings> options,
	IDistributedCache distributedCache)
	{
		_logger = logger;
		_activitySource = activitySource;
		_settings = options?.Value;
		_distributedCache = distributedCache;
        _meter = meterFactory.Create(new MeterOptions(typeof(DistributedTracingService).Namespace!)
		{
            Version = _activitySource.Version,
			Tags = new TagList
			{
				{ "code.namespace", GetType().Namespace },
				{ "code.class", GetType().Name }
			}
		});

        _activeActivitiesCounter = _meter.CreateUpDownCounter<int>(
            name: "distributed_tracing.active_activities",
            unit: "{activity}",
            description: "Number of active distributed tracing activities");

        _activitiesCreatedCounter = _meter.CreateCounter<long>(
            name: "distributed_tracing.activities_created",
            unit: "{activity}",
            description: "Total number of activities created");

        _cacheMissCounter = _meter.CreateCounter<long>(
            name: "distributed_tracing.cache_misses",
            unit: "{miss}",
            description: "Number of cache misses when continuing activities");

        _activityDurationHistogram = _meter.CreateHistogram<double>(
            name: "distributed_tracing.activity_duration",
            unit: "ms",
            description: "Duration of completed activities");
	}

    /// <inheritdoc />
    public ActivityScope StartActivity(
        string activityName,
        ActivityKind activityKind = ActivityKind.Internal,
        string? parentId = null,
        string? cacheKey = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);

        var activity = CreateAndStartActivity(activityName, activityKind, parentId, tags);

        // Store in cache if key provided (initiator role)
        if (!string.IsNullOrWhiteSpace(cacheKey) && activity is not null)
        {
            CacheActivityId(cacheKey, activity.Id!);
        }

        return WrapActivity(activity);
    }

    /// <inheritdoc />
    public ActivityScope ContinueActivity(
        string activityName,
        string cacheKey,
        ActivityKind activityKind = ActivityKind.Internal,
        string? fallbackParentId = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        // Lookup only - NEVER store (multi-instance safety)
        var parentId = GetCachedActivityId(cacheKey);

        if (string.IsNullOrEmpty(parentId))
        {
            _cacheMissCounter.Add(1, new TagList { { "cache_key_prefix", GetKeyPrefix(cacheKey) } });

            if (!string.IsNullOrEmpty(fallbackParentId))
            {
                _logger.LogDebug(
                    "Cache miss for key '{CacheKey}', using fallback parent ID for activity '{ActivityName}'",
                    cacheKey, activityName);
                parentId = fallbackParentId;
            }
            else
            {
                _logger.LogWarning(
                    "Cache miss for key '{CacheKey}' and no fallback. Activity '{ActivityName}' will start as new trace",
                    cacheKey, activityName);
            }
        }

        var activity = CreateAndStartActivity(activityName, activityKind, parentId, tags);
        return WrapActivity(activity);
    }

    /// <inheritdoc />
    public ActivityScope CreateLinkedActivity(
        string activityName,
        ActivityContext linkedContext,
        ActivityKind activityKind = ActivityKind.Internal,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);

        var links = new[] { new ActivityLink(linkedContext) };
        var activity = _activitySource.CreateActivity(
            activityName,
            activityKind,
            parentContext: default,
            tags: tags,
            links: links);

        activity?.Start();
        RecordActivityStart(activity);

        return WrapActivity(activity);
    }

    private Activity? CreateAndStartActivity(
        string activityName,
        ActivityKind activityKind,
        string? parentId,
        IEnumerable<KeyValuePair<string, object?>>? tags)
    {
        Activity? activity;

        if (!string.IsNullOrEmpty(parentId))
        {
            // Try W3C trace context parsing first
            if (ActivityContext.TryParse(parentId, null, out var parentContext))
            {
                activity = _activitySource.CreateActivity(
                    activityName,
                    activityKind,
                    parentContext: parentContext,
                    tags: tags);
            }
            else
            {
                // Fallback for legacy/simple parent ID format
                activity = _activitySource.CreateActivity(activityName, activityKind, parentId);
                if (activity is not null && tags is not null)
                {
                    foreach (var tag in tags)
                    {
                        activity.SetTag(tag.Key, tag.Value);
                    }
                }
            }
        }
        else
        {
            activity = _activitySource.CreateActivity(activityName, activityKind, parentId:null, tags: tags);
        }

        activity?.Start();
        RecordActivityStart(activity);

        return activity;
    }

    private void RecordActivityStart(Activity? activity)
    {
        if (activity is null) return;

        _activeActivitiesCounter.Add(1);
        _activitiesCreatedCounter.Add(1);
        _activeActivityIds.TryAdd(activity.Id!, DateTimeOffset.UtcNow);

        _logger.LogDebug(
            "Started activity '{ActivityName}' TraceId={TraceId}, SpanId={SpanId}, ParentSpanId={ParentSpanId}",
            activity.DisplayName,
            activity.TraceId,
            activity.SpanId,
            activity.ParentSpanId);
    }

    private void RecordActivityStop(Activity activity)
    {
        _activeActivitiesCounter.Add(-1);

        if (_activeActivityIds.TryRemove(activity.Id!, out var startTime))
        {
            var duration = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
            _activityDurationHistogram.Record(duration, new TagList
            {
                { "activity.name", activity.DisplayName },
                { "activity.status", activity.Status.ToString() }
            });
        }

        _logger.LogDebug(
            "Stopped activity '{ActivityName}' TraceId={TraceId}, SpanId={SpanId}, Status={Status}",
            activity.DisplayName,
            activity.TraceId,
            activity.SpanId,
            activity.Status);
    }

    private ActivityScope WrapActivity(Activity? activity)
    {
        return activity is null
            ? ActivityScope.Empty
            : new ActivityScope(activity, RecordActivityStop);
    }

    private string? GetCachedActivityId(string cacheKey)
    {
        try
        {
            return _distributedCache.GetString(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve cached activity ID for key '{CacheKey}'", cacheKey);
            return null;
        }
    }

    private void CacheActivityId(string cacheKey, string activityId)
    {
        try
        {
            _distributedCache.SetString(cacheKey, activityId, new DistributedCacheEntryOptions
            {
                SlidingExpiration = _settings.CacheSlidingExpiration,
                AbsoluteExpirationRelativeToNow = _settings.CacheAbsoluteExpiration
            });

            _logger.LogDebug("Cached activity ID for key '{CacheKey}'", cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache activity ID for key '{CacheKey}'", cacheKey);
        }
    }

    private static string GetKeyPrefix(string cacheKey)
    {
        var separatorIndex = cacheKey.IndexOf(':');
        return separatorIndex > 0 ? cacheKey[..separatorIndex] : "unknown";
    }

    public void Dispose()
    {
        _meter.Dispose();

        if (_activeActivityIds.Count > 0)
        {
            _logger.LogWarning(
                "Disposing DistributedTracingService with {Count} active activities still running",
                _activeActivityIds.Count);
        }
    }
}
