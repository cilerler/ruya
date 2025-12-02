using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using Ruya.Primitives;

namespace Ruya.Services.DistributedLock.Telemetry;

/// <summary>
/// Provides metrics and telemetry for the lock manager.
/// Uses System.Diagnostics.Metrics for modern .NET telemetry.
/// </summary>
public sealed class DistributedLockMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _lockAcquiredCounter;
    private readonly Counter<long> _lockFailedCounter;
    private readonly Counter<long> _lockReleasedCounter;
    private readonly Histogram<double> _lockDurationHistogram;
    private readonly Histogram<double> _lockAcquisitionDurationHistogram;
    private readonly Counter<long> _heartbeatSuccessCounter;
    private readonly Counter<long> _heartbeatFailureCounter;
    private readonly UpDownCounter<long> _activeLocks;

	/// <summary>
	/// Initializes a new instance of the <see cref="DistributedLockMetrics"/> class.
	/// </summary>
	/// <param name="meterName">The meter name. Defaults to `Primitives.Startup.AssemblyName`.</param>
	public DistributedLockMetrics(string? meterName = null)
    {
		_meter = new Meter(meterName ?? Startup.AssemblyName, Startup.AssemblyVersion);

        // Counters
        _lockAcquiredCounter = _meter.CreateCounter<long>(
            "lock_acquired_total",
            description: "Total number of locks successfully acquired");

        _lockFailedCounter = _meter.CreateCounter<long>(
            "lock_failed_total",
            description: "Total number of lock acquisitions that failed");

        _lockReleasedCounter = _meter.CreateCounter<long>(
            "lock_released_total",
            description: "Total number of locks successfully released");

        _heartbeatSuccessCounter = _meter.CreateCounter<long>(
            "heartbeat_success_total",
            description: "Total number of successful heartbeat extensions");

        _heartbeatFailureCounter = _meter.CreateCounter<long>(
            "heartbeat_failure_total",
            description: "Total number of failed heartbeat extensions");

        // Histograms
        _lockDurationHistogram = _meter.CreateHistogram<double>(
            "lock_duration_ms",
            unit: "ms",
            description: "Duration of lock hold time in milliseconds");

        _lockAcquisitionDurationHistogram = _meter.CreateHistogram<double>(
            "lock_acquisition_duration_ms",
            unit: "ms",
            description: "Time taken to acquire a lock in milliseconds");

        // Gauge (UpDownCounter)
        _activeLocks = _meter.CreateUpDownCounter<long>(
            "active_locks",
            description: "Current number of active locks held");
    }

    /// <summary>
    /// Records a successful lock acquisition.
    /// </summary>
    /// <param name="providerType">The type of lock provider (e.g., "InMemory", "Redis", "SqlServer").</param>
    /// <param name="durationMs">The time taken to acquire the lock in milliseconds.</param>
    public void RecordLockAcquired(string providerType, double durationMs)
    {
        _lockAcquiredCounter.Add(1, new KeyValuePair<string, object?>("provider", providerType));
        _lockAcquisitionDurationHistogram.Record(durationMs, new KeyValuePair<string, object?>("provider", providerType));
        _activeLocks.Add(1, new KeyValuePair<string, object?>("provider", providerType));
    }

    /// <summary>
    /// Records a failed lock acquisition.
    /// </summary>
    /// <param name="providerType">The type of lock provider.</param>
    /// <param name="reason">The reason for failure (e.g., "AlreadyLocked", "ProviderError").</param>
    public void RecordLockFailed(string providerType, string reason)
    {
        _lockFailedCounter.Add(1,
            new KeyValuePair<string, object?>("provider", providerType),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// Records a successful lock release.
    /// </summary>
    /// <param name="providerType">The type of lock provider.</param>
    /// <param name="holdDurationMs">The time the lock was held in milliseconds.</param>
    public void RecordLockReleased(string providerType, double holdDurationMs)
    {
        _lockReleasedCounter.Add(1, new KeyValuePair<string, object?>("provider", providerType));
        _lockDurationHistogram.Record(holdDurationMs, new KeyValuePair<string, object?>("provider", providerType));
        _activeLocks.Add(-1, new KeyValuePair<string, object?>("provider", providerType));
    }

    /// <summary>
    /// Records a successful heartbeat extension.
    /// </summary>
    /// <param name="providerType">The type of lock provider.</param>
    public void RecordHeartbeatSuccess(string providerType)
    {
        _heartbeatSuccessCounter.Add(1, new KeyValuePair<string, object?>("provider", providerType));
    }

    /// <summary>
    /// Records a failed heartbeat extension.
    /// </summary>
    /// <param name="providerType">The type of lock provider.</param>
    public void RecordHeartbeatFailure(string providerType)
    {
        _heartbeatFailureCounter.Add(1, new KeyValuePair<string, object?>("provider", providerType));
    }

    /// <summary>
    /// Creates a stopwatch to measure lock operations.
    /// </summary>
    /// <returns>A high-resolution stopwatch.</returns>
    public static Stopwatch CreateStopwatch() => Stopwatch.StartNew();

    /// <inheritdoc />
    public void Dispose()
    {
        _meter.Dispose();
    }
}
