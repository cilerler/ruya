using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

/// <summary>
/// Configuration settings for the data protection server.
/// </summary>
public sealed class DataProtectionSettings
{
    /// <summary>
    /// Configuration section name for binding.
    /// </summary>
    public const string ConfigurationSectionName = nameof(DataProtectionSettings);

    /// <summary>
    /// The application name used to isolate data protection keys.
    /// </summary>
    [Required]
    public required string ApplicationName { get; set; }

    /// <summary>
    /// Default key lifetime in days. Keys will be rotated after this period.
    /// </summary>
    [Range(1, 365)]
    public int DefaultKeyLifetime { get; set; } = 90;

    /// <summary>
    /// Dictionary of named purposes for creating purpose-specific protectors.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> Purposes { get; } = new();

    /// <summary>
    /// The connection string key name in the ConnectionStrings configuration section.
    /// </summary>
    [Required]
    public required string ConnectionStringKey { get; set; }

    /// <summary>
    /// The resolved Redis connection string. Server mode resolves it from
    /// <see cref="ConnectionStringKey"/>; remote client mode receives it at runtime.
    /// </summary>
    /// <remarks>
    /// Remote clients must treat this as a runtime credential and must not persist, log, or trace it.
    /// </remarks>
    [JsonInclude]
    public string ConnectionString { get; internal set; } = null!;

    /// <summary>
    /// The Redis key used to store data protection keys.
    /// </summary>
    [Required]
    public required string CacheKey { get; set; }
}
