using System;
using System.Collections.Generic;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Represents a message envelope containing the message payload and metadata
/// </summary>
/// <typeparam name="TMessage">The type of the message payload</typeparam>
public sealed class MessageEnvelope<TMessage> where TMessage : class
{
    /// <summary>
    /// Unique identifier for this message
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Correlation ID for tracking related messages across services
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Causation ID indicating which message caused this message
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    /// Source system or service that originated this message
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Message type identifier
    /// </summary>
    public required string MessageType { get; init; }

    /// <summary>
    /// Timestamp when the message was created
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Custom headers for the message
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// The actual message payload
    /// </summary>
    public required TMessage Payload { get; init; }

    /// <summary>
    /// Message priority (0-255, where higher values indicate higher priority)
    /// </summary>
    public byte Priority { get; init; }

    /// <summary>
    /// Time-to-live for the message
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>
    /// Delivery delay for the message
    /// </summary>
    public TimeSpan? DeliveryDelay { get; init; }

    /// <summary>
    /// Whether the message should be persisted
    /// </summary>
    public bool Persistent { get; init; } = true;
}
