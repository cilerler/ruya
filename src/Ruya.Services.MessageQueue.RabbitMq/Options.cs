using System;
using Microsoft.Extensions.Options;

namespace Ruya.Services.MessageQueue.RabbitMq;

/// <summary>
/// RabbitMQ-specific configuration options
/// </summary>
public sealed class RabbitMQOptions
{
    /// <summary>
    /// Configuration section used by the parameterless registration overload.
    /// </summary>
    public const string ConfigurationSectionName = $"{nameof(Ruya.Services.MessageQueue)}:RabbitMQ";

    /// <summary>
    /// RabbitMQ host
    /// </summary>
    public string Host { get; set; } = null!;

    /// <summary>
    /// RabbitMQ port
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Virtual host
    /// </summary>
    public string VirtualHost { get; set; } = null!;

    /// <summary>
    /// Username
    /// </summary>
    public string Username { get; set; } = null!;

    /// <summary>
    /// Password
    /// </summary>
    public string Password { get; set; } = null!;

    /// <summary>
    /// Whether to use SSL/TLS
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Connection timeout
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Heartbeat interval
    /// </summary>
    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether to enable automatic connection recovery
    /// </summary>
    public bool AutomaticRecoveryEnabled { get; set; } = true;

    /// <summary>
    /// Network recovery interval
    /// </summary>
    public TimeSpan NetworkRecoveryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Channel pool size
    /// </summary>
    public int ChannelPoolSize { get; set; } = 10;

    /// <summary>
    /// Whether to auto-create topology (exchanges, queues, bindings)
    /// </summary>
    public bool AutoCreateTopology { get; set; } = true;

    /// <summary>
    /// Default exchange type
    /// </summary>
    public string DefaultExchangeType { get; set; } = "topic";

    /// <summary>
    /// Whether to use publisher confirms
    /// </summary>
    public bool UsePublisherConfirms { get; set; } = true;

    /// <summary>
    /// Publisher confirm timeout
    /// </summary>
    public TimeSpan PublisherConfirmTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Prefetch count for consumers
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Compatibility setting for RabbitMQ Streams. The provider rejects this setting when enabled.
    /// </summary>
    public bool UseStreams { get; set; } = false;

    /// <summary>
    /// Compatibility settings for RabbitMQ Streams. The provider rejects non-null stream settings.
    /// </summary>
    public StreamOptions? StreamOptions { get; set; }
}

/// <summary>
/// Compatibility shape for RabbitMQ Stream configuration. Streams are not implemented by this provider.
/// </summary>
public sealed class StreamOptions
{
    /// <summary>
    /// Maximum stream size in bytes
    /// </summary>
    public long? MaxLengthBytes { get; set; }

    /// <summary>
    /// Maximum stream age
    /// </summary>
    public TimeSpan? MaxAge { get; set; }

    /// <summary>
    /// Stream segment size
    /// </summary>
    public long SegmentSize { get; set; } = 500_000_000; // 500 MB

    /// <summary>
    /// Initial offset for consumers
    /// </summary>
    public string InitialOffset { get; set; } = "next"; // "first", "last", "next", or specific offset
}

/// <summary>
/// Validates RabbitMQOptions
/// </summary>
public sealed class RabbitMQOptionsValidator : IValidateOptions<RabbitMQOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMQOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return ValidateOptionsResult.Fail("Host is required");
        }

        if (options.Port <= 0 || options.Port > 65535)
        {
            return ValidateOptionsResult.Fail("Port must be between 1 and 65535");
        }

        if (string.IsNullOrWhiteSpace(options.VirtualHost))
        {
            return ValidateOptionsResult.Fail("VirtualHost is required");
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            return ValidateOptionsResult.Fail("Username is required");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            return ValidateOptionsResult.Fail("Password is required");
        }

        if (options.ConnectionTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("ConnectionTimeout must be greater than zero");
        }

        if (options.ChannelPoolSize < 1)
        {
            return ValidateOptionsResult.Fail("ChannelPoolSize must be at least 1");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultExchangeType))
        {
            return ValidateOptionsResult.Fail("DefaultExchangeType is required");
        }

        if (options.UsePublisherConfirms && options.PublisherConfirmTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("PublisherConfirmTimeout must be greater than zero when publisher confirms are enabled");
        }

        if (options.UseStreams || options.StreamOptions is not null)
        {
            return ValidateOptionsResult.Fail("RabbitMQ Streams are not supported by this provider");
        }

        return ValidateOptionsResult.Success;
    }
}
