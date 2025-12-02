using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.DistributedLock.Abstractions;

/// <summary>
/// Abstraction for distributed lock providers.
/// Implements the Strategy pattern to allow different backend implementations.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Attempts to acquire a distributed lock.
    /// </summary>
    /// <param name="lockKey">The unique identifier for the lock.</param>
    /// <param name="lockValue">The value to associate with the lock (typically a unique instance identifier).</param>
    /// <param name="expiry">The time after which the lock will automatically expire.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the lock was acquired; otherwise, false.</returns>
    Task<bool> AcquireLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends the expiry time of an existing lock.
    /// </summary>
    /// <param name="lockKey">The unique identifier for the lock.</param>
    /// <param name="lockValue">The value associated with the lock.</param>
    /// <param name="expiry">The new expiry time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the lock was successfully extended; otherwise, false.</returns>
    Task<bool> ExtendLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a distributed lock.
    /// </summary>
    /// <param name="lockKey">The unique identifier for the lock.</param>
    /// <param name="lockValue">The value associated with the lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the lock was successfully released; otherwise, false.</returns>
    Task<bool> ReleaseLockAsync(
        string lockKey,
        string lockValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a lock exists.
    /// </summary>
    /// <param name="lockKey">The unique identifier for the lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the lock exists; otherwise, false.</returns>
    Task<bool> LockExistsAsync(
        string lockKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the name of the provider implementation.
    /// Used for telemetry, logging, and diagnostics.
    /// </summary>
    /// <returns>The provider name (e.g., "Redis", "SqlServer", "InMemory").</returns>
    string GetProviderName();
}
