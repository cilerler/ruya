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
    private const string _activitySourceName =
        $"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.DistributedLock)}";
    private static readonly ActivitySource _activitySource = new(_activitySourceName);
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
    public Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue = null,
        LockOptions? options = null)
        => AcquireAndExecuteWithLockAsync(callback, lockKey, lockValue, options, CancellationToken.None);

    /// <inheritdoc />
    public async Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue,
        LockOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        LockValidation.ValidateLockKey(lockKey);
        cancellationToken.ThrowIfCancellationRequested();

        // Every acquisition needs its own fencing owner. Reusing a process-wide value lets a
        // delayed release from an expired acquisition remove a later acquisition by this process.
        if (lockValue is not null)
        {
            LockValidation.ValidateLockValue(lockValue);
        }

        lockValue = CreateOwnerId(lockValue);

        options ??= LockOptions.Default;
        var internalLockKey = GetInternalLockKey(lockKey);
        var expiry = options.CustomExpiry ?? _settings.LockExpiry;
        LockValidation.ValidateExpiry(expiry);

        if (options.EnableHeartbeat)
        {
            TimeSpan heartbeatInterval = options.HeartbeatInterval ?? TimeSpan.FromTicks(expiry.Ticks / 3);
            LockValidation.ValidateExpiry(heartbeatInterval, nameof(options.HeartbeatInterval));
            if (heartbeatInterval >= expiry)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "HeartbeatInterval must be shorter than the lock expiry.");
            }
        }

        // Optimized scope logging
        using (_logger.BeginLockScope(internalLockKey))
        {
            var providerType = GetProviderType();
            using Activity? activity = _activitySource.StartActivity("distributed-lock.execute", ActivityKind.Internal);
            activity?.SetTag("lock.provider", providerType);
            var acquisitionStopwatch = Stopwatch.StartNew();

            try
            {
                // Try to acquire the lock directly
                // No need to check existence first - that would create a TOCTOU race condition
                bool lockAcquired = await _lockProvider.AcquireLockAsync(
                    internalLockKey,
                    lockValue,
                    expiry,
                    cancellationToken);

                acquisitionStopwatch.Stop();

                if (!lockAcquired)
                {
                    _logger.LogLockAcquisitionFailed(internalLockKey);

                    // Record failed acquisition
                    _metrics?.RecordLockFailed(providerType, LockStatus.AlreadyLocked.ToString());
                    activity?.SetStatus(ActivityStatusCode.Error, "already_locked");

                    return LockResult.Failed(LockStatus.AlreadyLocked, "Failed to acquire lock");
                }

                _logger.LogLockAcquired(internalLockKey);

                // Record successful acquisition
                _metrics?.RecordLockAcquired(providerType, acquisitionStopwatch.Elapsed.TotalMilliseconds);

                // Execute with heartbeat
                LockResult result = await ExecuteWithHeartbeatAsync(
                    callback,
                    internalLockKey,
                    lockValue,
                    expiry,
                    options,
                    providerType,
                    cancellationToken);
                activity?.SetStatus(result.IsSuccess ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogLockExecutionError(ex, internalLockKey);

                // Record provider error
                _metrics?.RecordLockFailed(providerType, LockStatus.ProviderError.ToString());
                activity?.SetStatus(ActivityStatusCode.Error, "provider_error");

                return LockResult.Failed(
                    LockStatus.ProviderError,
                    "The lock provider could not complete the operation.");
            }
        }
    }

    private async Task<LockResult> ExecuteWithHeartbeatAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        LockOptions options,
        string providerType,
        CancellationToken cancellationToken)
    {
        // Track lock hold duration
        var lockHoldStopwatch = Stopwatch.StartNew();

        // Create cancellation token for heartbeat and callback
        // This CTS is used to cancel the heartbeat loop AND the callback if heartbeat fails
        using var masterCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start heartbeat task if enabled
        Task<bool>? heartbeatTask = null;
        if (options.EnableHeartbeat)
        {
            var heartbeatInterval = options.HeartbeatInterval ??
                                   TimeSpan.FromTicks(expiry.Ticks / 3);

            heartbeatTask = HeartbeatAsync(
                lockKey,
                lockValue,
                expiry,
                heartbeatInterval,
                providerType,
                masterCts);
        }

        LockResult result;
        bool releaseConfirmed = false;
        bool heartbeatLost = false;

        try
        {
            // Execute the callback with the cancellation token
            // If heartbeat fails, masterCts will be cancelled, which should propagate to the callback
            await callback(masterCts.Token);
            cancellationToken.ThrowIfCancellationRequested();

            if (masterCts.IsCancellationRequested)
            {
                _logger.LogTaskCancelledLockLoss(lockKey);
                result = LockResult.Failed(LockStatus.ExecutionFailed, "The lock was lost before the callback completed.");
            }
            else
            {
                _logger.LogTaskCompleted(lockKey);
                result = LockResult.Succeeded();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (masterCts.IsCancellationRequested)
        {
            if (masterCts.IsCancellationRequested)
            {
                _logger.LogTaskCancelledLockLoss(lockKey);
                result = LockResult.Failed(LockStatus.ExecutionFailed, "Task cancelled due to lock loss");
            }
            else
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogCallbackError(ex, lockKey);
            result = LockResult.Failed(LockStatus.ExecutionFailed, "The lock callback failed.");
        }
        finally
        {
            try
            {
                // Stop heartbeat before releasing the provider lock.
                heartbeatLost = await StopHeartbeatAsync(masterCts, heartbeatTask);
            }
            finally
            {
                // A user cancellation callback can throw while the heartbeat token is
                // cancelled. Provider cleanup must still be attempted unconditionally.
                lockHoldStopwatch.Stop();
                releaseConfirmed = await ReleaseLockAsync(
                    lockKey,
                    lockValue,
                    providerType,
                    lockHoldStopwatch.Elapsed.TotalMilliseconds);
            }
        }

        if (result.IsSuccess && heartbeatLost)
        {
            _logger.LogTaskCancelledLockLoss(lockKey);
            result = LockResult.Failed(
                LockStatus.ExecutionFailed,
                "The lock was lost before heartbeat shutdown completed.");
        }

        return result.IsSuccess && !releaseConfirmed
            ? LockResult.Failed(LockStatus.ProviderError, "The lock release could not be confirmed.")
            : result;
    }

    private async Task<bool> HeartbeatAsync(
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
                    await CancelHeartbeatTokenAsync(masterCts);
                    return true;
                }
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogHeartbeatCancelled(lockKey);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogHeartbeatError(ex, lockKey);

            // Record heartbeat failure on exception
            _metrics?.RecordHeartbeatFailure(providerType);

            // On unexpected error, we should also probably cancel to be safe
            // But for now, we'll assume transient errors might recover?
            // No, safety first: if we can't heartbeat, we can't guarantee lock.
            await CancelHeartbeatTokenAsync(masterCts);
            return true;
        }
    }

    private async Task<bool> StopHeartbeatAsync(
        CancellationTokenSource cts,
        Task<bool>? heartbeatTask)
    {
        if (heartbeatTask == null) return false;

        await CancelHeartbeatTokenAsync(cts);

        try
        {
            return await heartbeatTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when we cancel
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogHeartbeatCancellationError(ex);
            return true;
        }
    }

    private async Task CancelHeartbeatTokenAsync(CancellationTokenSource cts)
    {
        try
        {
            await cts.CancelAsync();
        }
        catch (Exception ex)
        {
            // Cancellation callbacks are application code and may throw. The token is
            // still cancelled; record the failure without stranding the provider lock.
            _logger.LogHeartbeatCancellationError(ex);
        }
    }

    private async Task<bool> ReleaseLockAsync(string lockKey, string lockValue, string providerType, double holdDurationMs)
    {
        _logger.LogReleasingLock(lockKey);

        try
        {
            bool released = await _lockProvider.ReleaseLockAsync(lockKey, lockValue, CancellationToken.None);

            if (released)
            {
                _logger.LogLockReleased(lockKey);

                // Record successful release with hold duration
                _metrics?.RecordLockReleased(providerType, holdDurationMs);
                return true;
            }
            else
            {
                _logger.LogLockReleaseFailed(lockKey);
                _metrics?.RecordLockReleaseFailed(providerType, holdDurationMs);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogLockReleaseError(ex, lockKey);
            _metrics?.RecordLockReleaseFailed(providerType, holdDurationMs);
            return false;
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
                $"InstanceName length: {_settings.InstanceName.Length}; LockKey length: {lockKey.Length}.",
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

    private static string CreateOwnerId(string? requestedPrefix)
    {
        const int acquisitionIdLength = 32;
        int maximumPrefixLength = LockValidation.MaxLockValueLength - acquisitionIdLength - 1;
        string prefix = requestedPrefix ?? $"{Environment.MachineName}-{Environment.ProcessId}";
        if (prefix.Length > maximumPrefixLength)
        {
            prefix = prefix[..maximumPrefixLength];
        }

        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private string GetProviderType()
    {
        return _lockProvider.GetProviderName();
    }
}
