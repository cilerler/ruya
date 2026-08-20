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
    /// <param name="lockValue">An optional diagnostic prefix. Ruya appends a unique per-acquisition identifier before passing the owner value to the provider.</param>
    /// <param name="options">Optional lock configuration options.</param>
    /// <returns>A <see cref="LockResult"/> indicating the outcome of the operation.</returns>
    Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue = null,
        LockOptions? options = null);

    /// <summary>
    /// Acquires a distributed lock and executes a callback while observing caller cancellation.
    /// </summary>
    /// <param name="callback">The function to execute while holding the lock.</param>
    /// <param name="lockKey">The unique identifier for the lock.</param>
    /// <param name="lockValue">An optional diagnostic prefix. Ruya appends a unique per-acquisition identifier before passing the owner value to the provider.</param>
    /// <param name="options">Optional lock configuration options.</param>
    /// <param name="cancellationToken">Cancels acquisition and the running callback.</param>
    /// <returns>A <see cref="LockResult"/> indicating the outcome of the operation.</returns>
    async Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue,
        LockOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        LockResult result = await AcquireAndExecuteWithLockAsync(
            async providerToken =>
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    providerToken,
                    cancellationToken);
                await callback(linkedCancellation.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            },
            lockKey,
            lockValue,
            options).ConfigureAwait(false);

        // A released 8.x implementation may translate callback cancellation into a
        // LockResult. The additive bridge must still preserve caller cancellation.
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
