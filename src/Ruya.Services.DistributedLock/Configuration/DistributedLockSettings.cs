using System;
using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.DistributedLock.Configuration;

/// <summary>
/// Configuration settings for the lock manager.
/// </summary>
public sealed class DistributedLockSettings
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string ConfigurationSectionName = "DistributedLock";

    /// <summary>
    /// Gets or sets the default lock expiry time.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan LockExpiry { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the instance name to prefix lock keys with.
    /// Useful for multi-tenant scenarios or environment separation.
    /// </summary>
    public string? InstanceName { get; set; }
}
