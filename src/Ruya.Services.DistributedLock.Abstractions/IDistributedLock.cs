using System;
using System.Threading;
using System.Threading.Tasks;
using Ruya.Services.DistributedLock.Abstractions.Models;

namespace Ruya.Services.DistributedLock.Abstractions;

/// <summary>
/// Manages distributed locks with automatic acquisition, heartbeat, and release.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Acquires a distributed lock and executes a callback function while holding the lock.
    /// The lock is automatically released after the callback completes.
    /// </summary>
    /// <param name="callback">The function to execute while holding the lock.</param>
    /// <param name="lockKey">The unique identifier for the lock.</param>
    /// <param name="lockValue">The value to associate with the lock. If not provided, defaults to "{MachineName}-{ProcessId}".</param>
    /// <param name="options">Optional lock configuration options.</param>
    /// <returns>A <see cref="LockResult"/> indicating the outcome of the operation.</returns>
    Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue = null,
        LockOptions? options = null);
}
