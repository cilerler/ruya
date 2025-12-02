using System;

namespace Ruya.Services.DistributedLock.Abstractions.Models;

/// <summary>
/// Configuration options for lock operations.
/// </summary>
public sealed class LockOptions
{
    /// <summary>
    /// Gets a custom expiry time for the lock.
    /// If not specified, the default expiry from settings will be used.
    /// </summary>
    public TimeSpan? CustomExpiry { get; init; }

    /// <summary>
    /// Gets a value indicating whether heartbeat is enabled.
    /// When enabled, the lock will be automatically extended while the callback is executing.
    /// Default is true.
    /// </summary>
    public bool EnableHeartbeat { get; init; } = true;

    /// <summary>
    /// Gets the interval at which the lock heartbeat runs.
    /// If not specified, defaults to 1/3 of the lock expiry time.
    /// </summary>
    public TimeSpan? HeartbeatInterval { get; init; }

    private static readonly LockOptions _default = new();
    private static readonly LockOptions _withoutHeartbeat = new() { EnableHeartbeat = false };

    /// <summary>
    /// Creates a new instance with default options.
    /// </summary>
    /// <remarks>
    /// This property returns a cached immutable instance to avoid unnecessary allocations.
    /// All properties are init-only and cannot be modified after creation.
    /// To customize options, create a new instance using object initializer syntax.
    /// </remarks>
    public static LockOptions Default => _default;

    /// <summary>
    /// Creates options with heartbeat disabled.
    /// Useful for short-lived operations where heartbeat overhead is unnecessary.
    /// </summary>
    /// <remarks>
    /// This property returns a cached immutable instance to avoid unnecessary allocations.
    /// All properties are init-only and cannot be modified after creation.
    /// To customize options, create a new instance using object initializer syntax.
    /// </remarks>
    public static LockOptions WithoutHeartbeat => _withoutHeartbeat;
}
