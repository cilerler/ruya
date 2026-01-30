using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Ruya.Diagnostics.DistributedTracing;

/// <summary>
/// Configuration settings for distributed tracing.
/// </summary>
public sealed class DistributedTracingSettings
{
    public const string ConfigurationSectionName = "DistributedTracing";

    /// <summary>
    /// Sliding expiration for cached trace context.
    /// Activity continues the trace if accessed within this window.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:30", "1.00:00:00", ErrorMessage = "CacheSlidingExpiration must be between 30 seconds and 24 hours")]
    public TimeSpan CacheSlidingExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Absolute expiration for cached trace context.
    /// Prevents orphaned cache entries from long-running operations.
    /// </summary>
    public TimeSpan? CacheAbsoluteExpiration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Enable detailed debug logging for activity lifecycle events.
    /// </summary>
    public bool EnableDebugLogging { get; set; }

    /// <summary>
    /// Custom tags to add to all activities created by this service.
    /// </summary>
    public Dictionary<string, string> DefaultTags { get; set; } = new();
}
