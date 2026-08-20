using System;
using Microsoft.Extensions.Options;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Redis-specific configuration options
/// </summary>
public sealed class RedisOptions
{
    public const string ConfigurationSectionName =
        $"{nameof(Ruya.Services.MessageQueue)}:{nameof(Ruya.Services.MessageQueue.Redis)}";

    /// <summary>
    /// Descriptive key used to resolve the Redis connection from the top-level
    /// <c>ConnectionStrings</c> catalog.
    /// </summary>
    public string? RedisConnectionStringKey { get; set; }

    /// <summary>
    /// Resolved Redis connection string. Retained for released 8.x typed-configuration compatibility;
    /// standard configuration should set <see cref="RedisConnectionStringKey"/> instead.
    /// Format: host:port,password=xxx,ssl=true
    /// </summary>
    public required string ConnectionString { get; set; }

    internal bool ConnectionStringResolvedFromCatalog { get; set; }

    /// <summary>
    /// Redis database number
    /// </summary>
    public int Database { get; set; } = 0;

    /// <summary>
    /// Connection timeout
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sync timeout for operations
    /// </summary>
    public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether to use Pub/Sub
    /// </summary>
    public bool UsePubSub { get; set; } = true;

    /// <summary>
    /// Whether to publish to Redis Streams. Stream subscription is not implemented.
    /// </summary>
    public bool UseStreams { get; set; } = false;

    /// <summary>
    /// Stream configuration
    /// </summary>
    public RedisStreamOptions? StreamOptions { get; set; }

    /// <summary>
    /// Key prefix for all message bus operations
    /// </summary>
    public string KeyPrefix { get; set; } = "msgbus:";

    /// <summary>
    /// Whether to retry failed connections
    /// </summary>
    public bool RetryOnFailure { get; set; } = true;

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Whether to abort on connection failure
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;
}

/// <summary>
/// Redis Stream configuration
/// </summary>
public sealed class RedisStreamOptions
{
    /// <summary>
    /// Maximum stream length
    /// </summary>
    public long? MaxLength { get; set; }

    /// <summary>
    /// Use approximate max length (~ operator in Redis)
    /// </summary>
    public bool UseApproximateMaxLength { get; set; } = true;

    /// <summary>
    /// Consumer group creation behavior
    /// </summary>
    public bool AutoCreateConsumerGroup { get; set; } = true;

    /// <summary>
    /// Default consumer group name
    /// </summary>
    public string DefaultConsumerGroup { get; set; } = "default";

    /// <summary>
    /// Block timeout for XREADGROUP
    /// </summary>
    public TimeSpan BlockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Number of messages to read per call
    /// </summary>
    public int Count { get; set; } = 10;

    /// <summary>
    /// Idle time before claiming pending messages
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to automatically claim pending messages
    /// </summary>
    public bool AutoClaimPendingMessages { get; set; } = true;
}

/// <summary>
/// Validates RedisOptions
/// </summary>
public sealed class RedisOptionsValidator : IValidateOptions<RedisOptions>
{
    public ValidateOptionsResult Validate(string? name, RedisOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail("ConnectionString is required");
        }

        if (options.Database < 0)
        {
            return ValidateOptionsResult.Fail("Database must be non-negative");
        }

        if (options.ConnectionTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("ConnectionTimeout must be greater than zero");
        }

        if (options.ConnectionTimeout.TotalMilliseconds > int.MaxValue)
        {
            return ValidateOptionsResult.Fail("ConnectionTimeout cannot exceed Int32.MaxValue milliseconds");
        }

        if (options.SyncTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("SyncTimeout must be greater than zero");
        }

        if (options.SyncTimeout.TotalMilliseconds > int.MaxValue)
        {
            return ValidateOptionsResult.Fail("SyncTimeout cannot exceed Int32.MaxValue milliseconds");
        }

        if (options.RetryCount < 0)
        {
            return ValidateOptionsResult.Fail("RetryCount must be non-negative");
        }

        if (string.IsNullOrWhiteSpace(options.KeyPrefix))
        {
            return ValidateOptionsResult.Fail("KeyPrefix is required");
        }

        if (options.UsePubSub == options.UseStreams)
        {
            return ValidateOptionsResult.Fail("Exactly one of UsePubSub or UseStreams must be enabled");
        }

        if (options.StreamOptions is { } streamOptions)
        {
            if (streamOptions.MaxLength is <= 0)
            {
                return ValidateOptionsResult.Fail("StreamOptions.MaxLength must be greater than zero when configured");
            }

            if (string.IsNullOrWhiteSpace(streamOptions.DefaultConsumerGroup))
            {
                return ValidateOptionsResult.Fail("StreamOptions.DefaultConsumerGroup is required");
            }

            if (streamOptions.BlockTimeout <= TimeSpan.Zero)
            {
                return ValidateOptionsResult.Fail("StreamOptions.BlockTimeout must be greater than zero");
            }

            if (streamOptions.Count < 1)
            {
                return ValidateOptionsResult.Fail("StreamOptions.Count must be at least one");
            }

            if (streamOptions.IdleTimeout <= TimeSpan.Zero)
            {
                return ValidateOptionsResult.Fail("StreamOptions.IdleTimeout must be greater than zero");
            }
        }

        return ValidateOptionsResult.Success;
    }
}

internal sealed class RedisConnectionStringCatalogValidator : IValidateOptions<RedisOptions>
{
    public ValidateOptionsResult Validate(string? name, RedisOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RedisConnectionStringKey))
        {
            return ValidateOptionsResult.Skip;
        }

        return !options.ConnectionStringResolvedFromCatalog
            ? ValidateOptionsResult.Fail(
                "RedisConnectionStringKey must identify a configured connection string.")
            : ValidateOptionsResult.Success;
    }
}
