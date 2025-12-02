using System;
using System.Collections.Generic;
using System.Diagnostics;

// ReSharper disable once CheckNamespace
namespace Ruya.Diagnostics.DistributedTracing;

/// <summary>
/// RAII wrapper for Activity lifecycle management.
/// Implements IDisposable for deterministic cleanup.
/// </summary>
public readonly struct ActivityScope : IDisposable
{
    private readonly Action<Activity>? _onDispose;

    public Activity? Activity { get; }
    public string? TraceId => Activity?.TraceId.ToString();
    public string? SpanId => Activity?.SpanId.ToString();
    public bool IsRecording => Activity?.IsAllDataRequested ?? false;

    public ActivityScope(Activity? activity, Action<Activity>? onDispose = null)
    {
        Activity = activity;
        _onDispose = onDispose;
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

    public void Dispose()
    {
        if (Activity is not null)
        {
            _onDispose?.Invoke(Activity);
            Activity.Stop();
            Activity.Dispose();
        }
    }

    public static ActivityScope Empty => new(null);
}
