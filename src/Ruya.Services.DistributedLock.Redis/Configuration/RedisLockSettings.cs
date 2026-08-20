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
    public const string ConfigurationSectionName =
        $"{nameof(Ruya.Services.DistributedLock)}:{nameof(Ruya.Services.DistributedLock.Redis)}";

    /// <summary>
    /// Gets the legacy Redis connection-string value.
    /// The key-based registration intentionally leaves this value unpopulated so credentials
    /// are resolved only at the provider use site. The member remains for 8.x API compatibility.
    /// </summary>
    public string ConnectionString { get; internal set; } = null!;

    /// <summary>
    /// Gets or sets the connection string key name used to retrieve the connection string from ConnectionStrings configuration section.
    /// </summary>
    public string ConnectionStringKey { get; set; } = null!;

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

    /// <summary>
    /// Gets or sets connection-string catalog keys for independent Redlock nodes.
    /// This is preferred over placing endpoint connection strings directly in provider settings.
    /// </summary>
    public string[]? RedlockConnectionStringKeys { get; set; }
}
