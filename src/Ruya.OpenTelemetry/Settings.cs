using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Comprehensive OpenTelemetry configuration settings.
/// </summary>
public sealed class OpenTelemetrySettings
{
    public const string ConfigurationSectionName = "OpenTelemetry";

    /// <summary>
    /// Service identification settings.
    /// </summary>
    public ServiceSettings Service { get; set; } = new();

    /// <summary>
    /// Sampling configuration for traces.
    /// </summary>
    public SamplingSettings Sampling { get; set; } = new();

    /// <summary>
    /// Batch processor settings for trace export.
    /// </summary>
    public BatchProcessorSettings BatchProcessor { get; set; } = new();

    /// <summary>
    /// HTTP instrumentation settings.
    /// </summary>
    public HttpInstrumentationSettings Http { get; set; } = new();

    /// <summary>
    /// SQL instrumentation settings.
    /// </summary>
    public SqlInstrumentationSettings Sql { get; set; } = new();

    /// <summary>
    /// Kubernetes resource detection settings.
    /// </summary>
    public KubernetesSettings Kubernetes { get; set; } = new();

    /// <summary>
    /// Meters to add to the metrics provider.
    /// </summary>
    public List<string> Meters { get; set; } =
    [
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.AspNetCore.Http.Connections",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Diagnostics",
        "Microsoft.AspNetCore.RateLimiting",
        "System.Net.Http",
        "System.Net.NameResolution",
        "System.Net.Security",
        "Microsoft.Extensions.Diagnostics.ResourceMonitoring",
        "Ruya.Diagnostics.DistributedTracing"
    ];

    /// <summary>
    /// Additional custom tags to add to all telemetry.
    /// </summary>
    public Dictionary<string, string> CustomTags { get; set; } = new();
}

public sealed class ServiceSettings
{
    /// <summary>
    /// Override service name. Defaults to assembly name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Override service version. Defaults to assembly version.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Service namespace for grouping related services.
    /// </summary>
    public string? Namespace { get; set; }
}

public sealed class SamplingSettings
{
    /// <summary>
    /// Sampler type: AlwaysOn, AlwaysOff, TraceIdRatio, ParentBased.
    /// </summary>
    public SamplerType Type { get; set; } = SamplerType.ParentBased;

    /// <summary>
    /// Sampling ratio for TraceIdRatio sampler (0.0 to 1.0).
    /// </summary>
    [Range(0.0, 1.0)]
    public double Ratio { get; set; } = 0.1;

    /// <summary>
    /// Root sampler type when using ParentBased.
    /// </summary>
    public SamplerType ParentBasedRootSampler { get; set; } = SamplerType.TraceIdRatio;
}

public enum SamplerType
{
    AlwaysOn,
    AlwaysOff,
    TraceIdRatio,
    ParentBased
}

public sealed class BatchProcessorSettings
{
    /// <summary>
    /// Maximum batch size before export.
    /// </summary>
    [Range(1, 10000)]
    public int MaxExportBatchSize { get; set; } = 512;

    /// <summary>
    /// Maximum queue size for buffering.
    /// </summary>
    [Range(1, 100000)]
    public int MaxQueueSize { get; set; } = 2048;

    /// <summary>
    /// Delay between exports.
    /// </summary>
    public TimeSpan ScheduledDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Timeout for export operations.
    /// </summary>
    public TimeSpan ExporterTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class HttpInstrumentationSettings
{
    /// <summary>
    /// Enable request body capture.
    /// </summary>
    public bool CaptureRequestBody { get; set; }

    /// <summary>
    /// Enable response body capture.
    /// </summary>
    public bool CaptureResponseBody { get; set; }

    /// <summary>
    /// Maximum body size to capture in bytes.
    /// </summary>
    [Range(0, 1048576)] // 1MB max
    public int MaxBodySizeBytes { get; set; } = 32768; // 32KB default

    /// <summary>
    /// Content types to capture. Empty = all text-based types.
    /// </summary>
    public List<string> AllowedContentTypes { get; set; } =
    [
        "application/json",
        "application/xml",
        "text/plain",
        "text/xml",
        "application/x-www-form-urlencoded"
    ];

    /// <summary>
    /// URL patterns to exclude from body capture (regex).
    /// </summary>
    public List<string> ExcludeUrlPatterns { get; set; } =
    [
        "/health",
        "/ready",
        "/metrics",
        "/swagger"
    ];

    /// <summary>
    /// Headers to redact from capture.
    /// </summary>
    public List<string> RedactedHeaders { get; set; } =
    [
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Auth-Token"
    ];

    /// <summary>
    /// JSON paths to redact from body (e.g., "$.password", "$.creditCard").
    /// </summary>
    public List<string> RedactedJsonPaths { get; set; } =
    [
        "$.password",
        "$.secret",
        "$.token",
        "$.apiKey",
        "$.creditCard",
        "$.ssn",
        "$.socialSecurityNumber"
    ];
}

public sealed class SqlInstrumentationSettings
{
    /// <summary>
    /// Record SQL exceptions in trace spans. Default is true.
    /// </summary>
    /// <remarks>
    /// Set to false to suppress SQL exceptions from appearing in telemetry.
    /// This is useful when exceptions are expected and handled in code
    /// (e.g., duplicate key violations from race conditions).
    /// </remarks>
    public bool RecordException { get; set; } = true;

    /// <summary>
    /// Capture SQL command text.
    /// </summary>
    public bool CaptureCommandText { get; set; } = true;

    /// <summary>
    /// Sanitize SQL parameters (replace values with ?).
    /// </summary>
    public bool SanitizeStatements { get; set; } = true;

    /// <summary>
    /// Maximum SQL statement length to capture.
    /// </summary>
    [Range(0, 50000)]
    public int MaxStatementLength { get; set; } = 4000;

    /// <summary>
    /// Patterns to detect and redact in SQL (case-insensitive).
    /// </summary>
    public List<string> SensitivePatterns { get; set; } =
    [
        @"password\s*=\s*'[^']*'",
        @"pwd\s*=\s*'[^']*'",
        @"secret\s*=\s*'[^']*'",
        @"ssn\s*=\s*'[^']*'",
        @"creditcard\s*=\s*'[^']*'"
    ];
}

public sealed class KubernetesSettings
{
    /// <summary>
    /// Enable Kubernetes resource detection.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Detect container information.
    /// </summary>
    public bool DetectContainer { get; set; } = true;

    /// <summary>
    /// Detect host information.
    /// </summary>
    public bool DetectHost { get; set; } = true;
}
