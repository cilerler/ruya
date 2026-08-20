using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

// ReSharper disable once CheckNamespace
namespace Ruya.Diagnostics.DistributedTracing;

/// <summary>
/// RAII wrapper for Activity lifecycle management.
/// Copies share one disposal state so an activity is stopped exactly once.
/// </summary>
[SuppressMessage("Performance", "CA1815", Justification = "This copy-safe lifecycle handle intentionally uses shared identity and does not define value equality.")]
public readonly struct ActivityScope : IDisposable
{
    private readonly ScopeState? _state;

    public Activity? Activity => _state?.Activity;
    public string? TraceId => Activity?.TraceId.ToString();
    public string? SpanId => Activity?.SpanId.ToString();
    public bool IsRecording => Activity?.IsAllDataRequested ?? false;

    public ActivityScope(Activity? activity, Action<Activity>? onDispose = null)
    {
        _state = activity is null ? null : new ScopeState(activity, onDispose);
    }

    /// <summary>
    /// Adds a tag to the activity if it exists and is recording.
    /// </summary>
    public ActivityScope SetTag(string key, object? value)
    {
        Activity?.SetTag(key, value);
        return this;
    }

    /// <summary>
    /// Adds an event to the activity timeline.
    /// </summary>
    public ActivityScope AddEvent(string name, DateTimeOffset? timestamp = null)
    {
        Activity?.AddEvent(new ActivityEvent(name, timestamp ?? DateTimeOffset.UtcNow));
        return this;
    }

    /// <summary>
    /// Sets the activity status.
    /// </summary>
    public ActivityScope SetStatus(ActivityStatusCode status, string? description = null)
    {
        Activity?.SetStatus(status, description);
        return this;
    }

    public void Dispose() => _state?.Dispose();

    public static ActivityScope Empty => default;

    [SuppressMessage("Usage", "CA2213", Justification = "Dispose atomically exchanges and disposes the Activity exactly once.")]
    private sealed class ScopeState(Activity activity, Action<Activity>? onDispose) : IDisposable
    {
        private Activity? _activity = activity;
        private Action<Activity>? _onDispose = onDispose;

        public Activity? Activity => Volatile.Read(ref _activity);

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _activity, null);
            if (current is null)
            {
                return;
            }

            var callback = Interlocked.Exchange(ref _onDispose, null);
            try
            {
                callback?.Invoke(current);
            }
            finally
            {
                current.Stop();
                current.Dispose();
            }
        }
    }
}
