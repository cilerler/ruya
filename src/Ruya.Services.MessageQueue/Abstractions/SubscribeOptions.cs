using System;
using System.Collections.Generic;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Options for subscribing to messages
/// </summary>
public sealed class SubscribeOptions
{
    /// <summary>
    /// Whether to automatically acknowledge messages after successful processing
    /// Default is false (manual acknowledgment for reliability)
    /// </summary>
    public bool AutoAck { get; set; }

    /// <summary>
    /// Number of messages to prefetch
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Consumer group name for load balancing across multiple consumers
    /// </summary>
    public string? ConsumerGroup { get; set; }

    /// <summary>
    /// Routing pattern for subscribing to messages
    /// If not specified, defaults to matching all messages on the topic.
    /// Supports wildcards: * (single word), # (zero or more words)
    ///
    /// Provider Support:
    /// - RabbitMQ: Full support with * and # wildcards for topic exchanges
    /// - Redis: Full support via PSUBSCRIBE (pattern mapped to Redis syntax)
    /// - InMemory: Full support (regex-based pattern matching for testing)
    /// - SQL Server: Not supported (subscribes to all messages on topic)
    ///
    /// Examples: "orders.*.created", "orders.#", "*.urgent", "logs.error.*"
    /// </summary>
    public string? RoutingPattern { get; set; }

    /// <summary>
    /// Multiple routing patterns for subscribing to messages
    /// Allows a single subscription to match multiple patterns.
    /// If specified, RoutingPattern is ignored.
    ///
    /// Provider Support:
    /// - RabbitMQ: Full support (creates multiple queue bindings)
    /// - Redis: Partial support (uses first pattern only, limitation of Redis Pub/Sub)
    /// - InMemory: Full support (matches against any pattern in the list)
    /// - SQL Server: Not supported
    ///
    /// Examples: ["orders.*.created", "orders.*.updated", "inventory.*.low_stock"]
    /// </summary>
    public List<string>? RoutingPatterns { get; set; }

    /// <summary>
    /// Maximum number of concurrent message handlers
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// Maximum priority for the queue (RabbitMQ specific)
    /// </summary>
    public int? MaxPriority { get; set; }

    /// <summary>
    /// Retry policy for failed messages
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// Dead letter queue configuration
    /// </summary>
    public DeadLetterQueueOptions? DeadLetterQueue { get; set; }

    /// <summary>
    /// For stream-based providers: starting position for message consumption
    /// </summary>
    public StreamPosition? StreamPosition { get; set; }

    /// <summary>
    /// For stream-based providers: consumer name for offset tracking
    /// </summary>
    public string? ConsumerName { get; set; }

    /// <summary>
    /// Whether unhandled exceptions thrown during message processing (deserialization failures,
    /// infrastructure faults, anything before the user handler returns a <see cref="MessageStatus"/>)
    /// should requeue the message back to the source queue. Default <c>false</c> — exceptions are
    /// treated as poison messages and rejected. The broker routes to the configured DLX, or drops the
    /// message if no DLX is configured.
    /// <para>Set to <c>true</c> only if you genuinely want infinite redelivery on every exception
    /// (e.g. for a known-transient infrastructure error path). Beware: a malformed message with this
    /// flag on creates a tight redelivery loop.</para>
    /// <para>Does not affect explicit <see cref="MessageResult.Retry"/> returns from the user handler —
    /// those still requeue (subject to <see cref="MaxDeliveryCount"/>).</para>
    /// </summary>
    public bool RequeueOnException { get; set; }

    /// <summary>
    /// Maximum number of deliveries before the broker rejects the message without requeue.
    /// Applies to both the <see cref="MessageStatus.Retry"/> result path and the unhandled-exception
    /// path (when <see cref="RequeueOnException"/> is true). Default <c>null</c> means no cap on the
    /// Retry path.
    /// <para>Provider note: RabbitMQ derives the live count from the <c>x-death</c> header which is
    /// only populated when the queue is configured with a Dead-Letter Exchange. If no DLX is wired
    /// (<see cref="DeadLetterQueue"/> is null), the count effectively maxes at 2 (first delivery + one
    /// redelivery) so this cap behaves as "max one retry."</para>
    /// </summary>
    public int? MaxDeliveryCount { get; set; }
}

/// <summary>
/// Retry policy for failed message processing
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Initial retry delay
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum retry delay
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Backoff multiplier for exponential backoff
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Whether to use exponential backoff
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;
}

/// <summary>
/// Dead letter queue configuration
/// </summary>
public sealed class DeadLetterQueueOptions
{
    /// <summary>
    /// Name of the dead letter queue
    /// </summary>
    public required string QueueName { get; set; }

    /// <summary>
    /// Maximum number of delivery attempts before sending to DLQ
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>
    /// Whether to enable dead letter queue
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Stream position for consuming messages from streams
/// </summary>
public enum StreamPosition
{
    /// <summary>
    /// Start from the beginning of the stream
    /// </summary>
    Beginning,

    /// <summary>
    /// Start from the end (only new messages)
    /// </summary>
    End,

    /// <summary>
    /// Resume from last checkpoint
    /// </summary>
    LastCheckpoint
}
