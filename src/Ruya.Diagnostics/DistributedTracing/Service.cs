using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(activitySource);
		ArgumentNullException.ThrowIfNull(meterFactory);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(distributedCache);

		_logger = logger;
		_activitySource = activitySource;
		_settings = options.Value;
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
    public async ValueTask<ActivityScope> StartActivityAsync(
        string activityName,
        ActivityKind activityKind = ActivityKind.Internal,
        string? parentId = null,
        string? cacheKey = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = WrapActivity(CreateAndStartActivity(activityName, activityKind, parentId, tags));
        try
        {
            if (!string.IsNullOrWhiteSpace(cacheKey) && scope.Activity is not null)
            {
                await CacheActivityIdAsync(cacheKey, scope.Activity.Id!, cancellationToken).ConfigureAwait(false);
            }

            return scope;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            scope.Dispose();
            throw;
        }
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
            _cacheMissCounter.Add(1);

            if (!string.IsNullOrEmpty(fallbackParentId))
            {
                _logger.CacheMissUsingFallback(activityName);
                parentId = fallbackParentId;
            }
            else
            {
                _logger.CacheMissWithoutFallback(activityName);
            }
        }

        var activity = CreateAndStartActivity(activityName, activityKind, parentId, tags);
        return WrapActivity(activity);
    }

    /// <inheritdoc />
    public async ValueTask<ActivityScope> ContinueActivityAsync(
        string activityName,
        string cacheKey,
        ActivityKind activityKind = ActivityKind.Internal,
        string? fallbackParentId = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        cancellationToken.ThrowIfCancellationRequested();

        var parentId = await GetCachedActivityIdAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(parentId))
        {
            _cacheMissCounter.Add(1);

            if (!string.IsNullOrEmpty(fallbackParentId))
            {
                _logger.CacheMissUsingFallback(activityName);
                parentId = fallbackParentId;
            }
            else
            {
                _logger.CacheMissWithoutFallback(activityName);
            }
        }

        return WrapActivity(CreateAndStartActivity(activityName, activityKind, parentId, tags));
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
            tags: MergeDefaultTags(tags),
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
                    tags: MergeDefaultTags(tags));
            }
            else
            {
                // Fallback for legacy/simple parent ID format
                activity = _activitySource.CreateActivity(activityName, activityKind, parentId);
                if (activity is not null)
                {
                    foreach (var tag in MergeDefaultTags(tags))
                    {
                        activity.SetTag(tag.Key, tag.Value);
                    }
                }
            }
        }
        else
        {
            activity = _activitySource.CreateActivity(
                activityName,
                activityKind,
                parentId: null,
                tags: MergeDefaultTags(tags));
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

        if (_settings.EnableDebugLogging)
        {
            _logger.ActivityStarted(
                activity.DisplayName,
                activity.TraceId,
                activity.SpanId,
                activity.ParentSpanId);
        }
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

        if (_settings.EnableDebugLogging)
        {
            _logger.ActivityStopped(
                activity.DisplayName,
                activity.TraceId,
                activity.SpanId,
                activity.Status);
        }
    }

    private ActivityScope WrapActivity(Activity? activity)
    {
        return activity is null
            ? ActivityScope.Empty
            : new ActivityScope(activity, RecordActivityStop);
    }

    [SuppressMessage("Design", "CA1031", Justification = "Trace-context cache failures must not fail the caller's business operation.")]
    private string? GetCachedActivityId(string cacheKey)
    {
        try
        {
            return _distributedCache.GetString(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.CacheReadFailed(ex);
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Trace-context cache failures must not fail the caller's business operation.")]
    private async Task<string?> GetCachedActivityIdAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            return await _distributedCache.GetStringAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.CacheReadFailed(ex);
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Trace-context cache failures must not fail the caller's business operation.")]
    private void CacheActivityId(string cacheKey, string activityId)
    {
        try
        {
            _distributedCache.SetString(cacheKey, activityId, new DistributedCacheEntryOptions
            {
                SlidingExpiration = _settings.CacheSlidingExpiration,
                AbsoluteExpirationRelativeToNow = _settings.CacheAbsoluteExpiration
            });

            if (_settings.EnableDebugLogging)
            {
                _logger.TraceContextCached();
            }
        }
        catch (Exception ex)
        {
            _logger.CacheWriteFailed(ex);
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Trace-context cache failures must not fail the caller's business operation.")]
    private async Task CacheActivityIdAsync(
        string cacheKey,
        string activityId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _distributedCache.SetStringAsync(cacheKey, activityId, new DistributedCacheEntryOptions
            {
                SlidingExpiration = _settings.CacheSlidingExpiration,
                AbsoluteExpirationRelativeToNow = _settings.CacheAbsoluteExpiration
            }, cancellationToken).ConfigureAwait(false);

            if (_settings.EnableDebugLogging)
            {
                _logger.TraceContextCached();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.CacheWriteFailed(ex);
        }
    }

    private IEnumerable<KeyValuePair<string, object?>> MergeDefaultTags(
        IEnumerable<KeyValuePair<string, object?>>? tags)
    {
        if (_settings.DefaultTags.Count == 0)
        {
            return tags ?? [];
        }

        var merged = _settings.DefaultTags.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value,
            StringComparer.Ordinal);

        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                merged[tag.Key] = tag.Value;
            }
        }

        return merged;
    }

    public void Dispose()
    {
        _meter.Dispose();

        if (!_activeActivityIds.IsEmpty)
        {
            _logger.ActiveActivitiesOnDispose(_activeActivityIds.Count);
        }

        GC.SuppressFinalize(this);
    }
}
