using System;
using Microsoft.Extensions.Options;

namespace Ruya.Services.MessageQueue.RabbitMq;

/// <summary>
/// RabbitMQ-specific configuration options
/// </summary>
public sealed class RabbitMQOptions
{
    /// <summary>
    /// RabbitMQ host
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// RabbitMQ port
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Virtual host
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Username
    /// </summary>
    public string Username { get; set; } = "guest";

    /// <summary>
    /// Password
    /// </summary>
    public string Password { get; set; } = "guest";

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
    /// Whether to use RabbitMQ Streams
    /// </summary>
    public bool UseStreams { get; set; } = false;

    /// <summary>
    /// Stream configuration
    /// </summary>
    public StreamOptions? StreamOptions { get; set; }
}

/// <summary>
/// RabbitMQ Stream configuration
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
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return ValidateOptionsResult.Fail("Host is required");
        }

        if (options.Port <= 0 || options.Port > 65535)
        {
            return ValidateOptionsResult.Fail("Port must be between 1 and 65535");
        }

        if (options.ConnectionTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("ConnectionTimeout must be greater than zero");
        }

        if (options.ChannelPoolSize < 1)
        {
            return ValidateOptionsResult.Fail("ChannelPoolSize must be at least 1");
        }

        return ValidateOptionsResult.Success;
    }
}
