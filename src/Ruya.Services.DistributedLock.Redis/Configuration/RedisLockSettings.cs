using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.DistributedLock.Redis.Configuration;

/// <summary>
/// Configuration settings for Redis-based distributed locks.
/// </summary>
public sealed class RedisLockSettings
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string ConfigurationSectionName = "DistributedLock:Redis";

    /// <summary>
    /// Gets or sets the Redis connection string.
    /// This is populated internally from ConnectionStrings configuration using the ConnectionStringKey.
    /// </summary>
    [Required]
    public string ConnectionString { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection string key name used to retrieve the connection string from ConnectionStrings configuration section.
    /// </summary>
    [Required]
    public string ConnectionStringKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sync timeout in milliseconds.
    /// </summary>
    [Range(1000, 30000)]
    public int SyncTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets a value indicating whether to abort on connect failure.
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;

    /// <summary>
    /// Gets or sets the list of connection strings for Redlock (multi-master) setup.
    /// If provided, these will be used instead of the single ConnectionString.
    /// </summary>
    public string[]? RedlockEndpoints { get; set; }
}
