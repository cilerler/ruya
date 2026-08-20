using System;
using System.Collections.Generic;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Options for publishing messages
/// </summary>
public sealed class PublishOptions
{
    /// <summary>
    /// Caller-assigned message identifier. When omitted, the provider generates an identifier.
    /// This option is not valid for batch publishing because every message in a batch needs a
    /// distinct identifier.
    /// </summary>
    public string? MessageId { get; set; }

    /// <summary>
    /// Message priority (0-255, where higher values indicate higher priority)
    /// Default is 0 (normal priority)
    /// </summary>
    public byte Priority { get; set; }

    /// <summary>
    /// Time-to-live for the message
    /// </summary>
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Delivery delay for the message
    /// </summary>
    public TimeSpan? DeliveryDelay { get; set; }

    /// <summary>
    /// Whether the message should be persisted to disk
    /// Default is true for durability
    /// </summary>
    public bool Persistent { get; set; } = true;

    /// <summary>
    /// Whether a provider with publisher-confirm support should wait for broker confirmation.
    /// A provider-level setting may disable publisher confirms entirely.
    /// </summary>
    public bool WaitForConfirmation { get; set; } = true;

    /// <summary>
    /// Correlation ID for tracking related messages
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Causation ID indicating which message caused this message
    /// </summary>
    public string? CausationId { get; set; }

    /// <summary>
    /// Source system or service that originated this message
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Custom headers for the message
    /// </summary>
    public Dictionary<string, object>? Headers { get; set; }

    /// <summary>
    /// Routing key for message routing
    /// If not specified, defaults to the topic name.
    ///
    /// Provider Support:
    /// - RabbitMQ: Full support with wildcards (* single word, # zero or more words) for topic exchanges
    /// - Redis: Full support (mapped to Redis channel names with : separator)
    /// - InMemory: Full support (regex-based pattern matching)
    /// - SQL Server: Not supported (messages routed by topic only)
    ///
    /// Examples: "orders.us.created", "orders:us:created" (Redis), "logs.error"
    /// </summary>
    public string? RoutingKey { get; set; }

    /// <summary>
    /// Timeout for this publish operation. When omitted, the message-queue default applies.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

}
