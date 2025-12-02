using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Abstractions.Models;
using Ruya.Services.DistributedLock.Common;
using Ruya.Services.DistributedLock.Configuration;
using Ruya.Services.DistributedLock.Telemetry;

namespace Ruya.Services.DistributedLock.Core;

/// <summary>
/// Manages distributed locks with automatic acquisition, heartbeat, and release.
/// Implements the Template Method pattern for lock lifecycle management.
/// </summary>
public sealed class DistributedLock : IDistributedLock
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<DistributedLock> _logger;
    private readonly DistributedLockSettings _settings;
    private readonly DistributedLockMetrics? _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedLock"/> class.
    /// </summary>
    /// <param name="lockProvider">The distributed lock provider.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="settings">The lock manager settings.</param>
    /// <param name="metrics">Optional metrics instance for telemetry.</param>
    public DistributedLock(
        IDistributedLockProvider lockProvider,
        ILogger<DistributedLock> logger,
        IOptions<DistributedLockSettings> settings,
        DistributedLockMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(lockProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);

        _lockProvider = lockProvider;
        _logger = logger;
        _settings = settings.Value;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue = null,
        LockOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);

        // Use default lock value if not provided
        lockValue ??= $"{Environment.MachineName}-{Environment.ProcessId}";
        ArgumentException.ThrowIfNullOrWhiteSpace(lockValue);

        options ??= LockOptions.Default;
        var internalLockKey = GetInternalLockKey(lockKey);

        // Optimized scope logging
        using (_logger.BeginLockScope(internalLockKey, lockValue))
        {
            var providerType = GetProviderType();
            var acquisitionStopwatch = Stopwatch.StartNew();

            try
            {
                // Try to acquire the lock directly
                // No need to check existence first - that would create a TOCTOU race condition
                var expiry = options.CustomExpiry ?? _settings.LockExpiry;
                bool lockAcquired = await _lockProvider.AcquireLockAsync(
                    internalLockKey,
                    lockValue,
                    expiry);

                acquisitionStopwatch.Stop();

                if (!lockAcquired)
                {
                    _logger.LogLockAcquisitionFailed(internalLockKey);

                    // Record failed acquisition
                    _metrics?.RecordLockFailed(providerType, LockStatus.AlreadyLocked.ToString());

                    return LockResult.Failed(LockStatus.AlreadyLocked, "Failed to acquire lock");
                }

                _logger.LogLockAcquired(internalLockKey);

                // Record successful acquisition
                _metrics?.RecordLockAcquired(providerType, acquisitionStopwatch.Elapsed.TotalMilliseconds);

                // Execute with heartbeat
                return await ExecuteWithHeartbeatAsync(
                    callback,
                    internalLockKey,
                    lockValue,
                    expiry,
                    options,
                    providerType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogLockExecutionError(ex, internalLockKey);

                // Record provider error
                _metrics?.RecordLockFailed(providerType, LockStatus.ProviderError.ToString());

                return LockResult.Failed(
                    LockStatus.ProviderError,
                    $"Provider error: {ex.Message}");
            }
        }
    }

    private async Task<LockResult> ExecuteWithHeartbeatAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        LockOptions options,
        string providerType)
    {
        // Track lock hold duration
        var lockHoldStopwatch = Stopwatch.StartNew();

        // Create cancellation token for heartbeat and callback
        // This CTS is used to cancel the heartbeat loop AND the callback if heartbeat fails
        using var masterCts = new CancellationTokenSource();

        // Start heartbeat task if enabled
        Task? heartbeatTask = null;
        if (options.EnableHeartbeat)
        {
            var heartbeatInterval = options.HeartbeatInterval ??
                                   TimeSpan.FromSeconds(expiry.TotalSeconds / 3);

            heartbeatTask = HeartbeatAsync(
                lockKey,
                lockValue,
                expiry,
                heartbeatInterval,
                providerType,
                masterCts);
        }

        try
        {
            // Execute the callback with the cancellation token
            // If heartbeat fails, masterCts will be cancelled, which should propagate to the callback
            await callback(masterCts.Token);
            _logger.LogTaskCompleted(lockKey);
            return LockResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            if (masterCts.IsCancellationRequested)
            {
                _logger.LogTaskCancelledLockLoss(lockKey);
                return LockResult.Failed(LockStatus.ExecutionFailed, "Task cancelled due to lock loss");
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCallbackError(ex, lockKey);
            return LockResult.Failed(LockStatus.ExecutionFailed, ex.Message);
        }
        finally
        {
            // Stop heartbeat
            await StopHeartbeatAsync(masterCts, heartbeatTask);

            // Stop tracking lock hold duration
            lockHoldStopwatch.Stop();

            // Release the lock
            await ReleaseLockAsync(lockKey, lockValue, providerType, lockHoldStopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task HeartbeatAsync(
        string lockKey,
        string lockValue,
        TimeSpan lockExpiry,
        TimeSpan interval,
        string providerType,
        CancellationTokenSource masterCts)
    {
        _logger.LogHeartbeatStarted(interval.TotalSeconds, lockKey);

        var cancellationToken = masterCts.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);

                // Extend the lock
                bool extended = await _lockProvider.ExtendLockAsync(
                    lockKey,
                    lockValue,
                    lockExpiry,
                    cancellationToken);

                if (extended)
                {
                    _logger.LogLockExtended(lockExpiry.TotalSeconds, lockKey);

                    // Record successful heartbeat
                    _metrics?.RecordHeartbeatSuccess(providerType);
                }
                else
                {
                    _logger.LogLockExtensionFailed(lockKey);

                    // Record failed heartbeat
                    _metrics?.RecordHeartbeatFailure(providerType);

                    // CRITICAL: Cancel the master token to abort the running callback
                    masterCts.Cancel();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogHeartbeatCancelled(lockKey);
        }
        catch (Exception ex)
        {
            _logger.LogHeartbeatError(ex, lockKey);

            // Record heartbeat failure on exception
            _metrics?.RecordHeartbeatFailure(providerType);

            // On unexpected error, we should also probably cancel to be safe
            // But for now, we'll assume transient errors might recover?
            // No, safety first: if we can't heartbeat, we can't guarantee lock.
             try { masterCts.Cancel(); } catch { /* ignore */ }
        }
    }

    private async Task StopHeartbeatAsync(
        CancellationTokenSource cts,
        Task? heartbeatTask)
    {
        if (heartbeatTask == null) return;

        cts.Cancel();

        try
        {
            await heartbeatTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when we cancel
        }
        catch (Exception ex)
        {
            _logger.LogHeartbeatCancellationError(ex);
        }
    }

    private async Task ReleaseLockAsync(string lockKey, string lockValue, string providerType, double holdDurationMs)
    {
        _logger.LogReleasingLock(lockKey);

        try
        {
            bool released = await _lockProvider.ReleaseLockAsync(lockKey, lockValue);

            if (released)
            {
                _logger.LogLockReleased(lockKey);

                // Record successful release with hold duration
                _metrics?.RecordLockReleased(providerType, holdDurationMs);
            }
            else
            {
                _logger.LogLockReleaseFailed(lockKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogLockReleaseError(ex, lockKey);
        }
    }

    private string GetInternalLockKey(string lockKey)
    {
        if (string.IsNullOrWhiteSpace(_settings.InstanceName))
        {
            return lockKey;
        }

        // Optimized string creation to avoid intermediate allocations
        // Format: "{InstanceName}:{lockKey}"
        int length = _settings.InstanceName.Length + 1 + lockKey.Length;

        if (length > LockValidation.MaxLockKeyLength)
        {
             throw new ArgumentException(
                $"Combined lock key (InstanceName + lockKey) exceeds maximum length of {LockValidation.MaxLockKeyLength} characters. " +
                $"Actual length: {length}. " +
                $"InstanceName: '{_settings.InstanceName}' ({_settings.InstanceName.Length} chars), " +
                $"LockKey: '{lockKey}' ({lockKey.Length} chars)",
                nameof(lockKey));
        }

        return string.Create(length, (_settings.InstanceName, lockKey), (span, state) =>
        {
            var (instanceName, key) = state;
            instanceName.AsSpan().CopyTo(span);
            span[instanceName.Length] = ':';
            key.AsSpan().CopyTo(span.Slice(instanceName.Length + 1));
        });
    }

    private string GetProviderType()
    {
        return _lockProvider.GetProviderName();
    }
}
