using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Ruya.Services.DistributedLock.Core;

/// <summary>
/// High-performance logging extensions using source generators.
/// </summary>
internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Failed to acquire lock. Lock may be held by another instance. [LockKey = {LockKey}]")]
    public static partial void LogLockAcquisitionFailed(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Lock acquired. [LockKey = {LockKey}]")]
    public static partial void LogLockAcquired(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Error during lock execution. [LockKey = {LockKey}]")]
    public static partial void LogLockExecutionError(this ILogger logger, Exception ex, string lockKey);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "Task completed. [LockKey = {LockKey}]")]
    public static partial void LogTaskCompleted(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Task cancelled due to lock loss (heartbeat failure). [LockKey = {LockKey}]")]
    public static partial void LogTaskCancelledLockLoss(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "Error executing callback. [LockKey = {LockKey}]")]
    public static partial void LogCallbackError(this ILogger logger, Exception ex, string lockKey);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Debug,
        Message = "Heartbeat started. Interval: {Interval}s [LockKey = {LockKey}]")]
    public static partial void LogHeartbeatStarted(this ILogger logger, double interval, string lockKey);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Debug,
        Message = "Lock extended by {Expiry}s [LockKey = {LockKey}]")]
    public static partial void LogLockExtended(this ILogger logger, double expiry, string lockKey);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Failed to extend lock. Lock may have been released or taken by another instance. Cancelling operation. [LockKey = {LockKey}]")]
    public static partial void LogLockExtensionFailed(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Debug,
        Message = "Heartbeat cancelled. [LockKey = {LockKey}]")]
    public static partial void LogHeartbeatCancelled(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Error,
        Message = "Heartbeat error. [LockKey = {LockKey}]")]
    public static partial void LogHeartbeatError(this ILogger logger, Exception ex, string lockKey);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "Unexpected error during heartbeat cancellation")]
    public static partial void LogHeartbeatCancellationError(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Debug,
        Message = "Releasing lock. [LockKey = {LockKey}]")]
    public static partial void LogReleasingLock(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Information,
        Message = "Lock released. [LockKey = {LockKey}]")]
    public static partial void LogLockReleased(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Warning,
        Message = "Failed to release lock. [LockKey = {LockKey}]")]
    public static partial void LogLockReleaseFailed(this ILogger logger, string lockKey);

    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Error,
        Message = "Error releasing lock. [LockKey = {LockKey}]")]
    public static partial void LogLockReleaseError(this ILogger logger, Exception ex, string lockKey);

    // Scope definition
    public static IDisposable? BeginLockScope(this ILogger logger, string lockKey, string lockValue)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["LockKey"] = lockKey,
            ["LockValue"] = lockValue
        });
    }
}
