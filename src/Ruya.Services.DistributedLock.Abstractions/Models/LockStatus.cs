using System;

namespace Ruya.Services.DistributedLock.Abstractions.Models;

/// <summary>
/// Represents the status of a lock operation.
/// </summary>
public enum LockStatus
{
    /// <summary>
    /// The lock operation succeeded.
    /// </summary>
    Success,

    /// <summary>
    /// The lock is already held by another instance.
    /// </summary>
    AlreadyLocked,

    /// <summary>
    /// Failed to acquire the lock.
    /// </summary>
    /// <remarks>
    /// This status is not currently used in the implementation. Lock acquisition failures
    /// return <see cref="AlreadyLocked"/> status instead. This value is retained for
    /// backward compatibility but may be removed in a future major version.
    /// </remarks>
    [Obsolete("This status is not currently used. Lock acquisition failures return AlreadyLocked status instead.")]
    AcquireFailed,

    /// <summary>
    /// An error occurred with the lock provider (e.g., Redis, SQL Server).
    /// </summary>
    ProviderError,

    /// <summary>
    /// An error occurred during callback execution.
    /// </summary>
    ExecutionFailed
}
