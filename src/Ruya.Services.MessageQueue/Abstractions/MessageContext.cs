using System;
using System.Collections.Generic;
using System.Threading;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Provides context information for a received message
/// </summary>
/// <typeparam name="TMessage">The type of the message</typeparam>
public sealed class MessageContext<TMessage> where TMessage : class
{
    /// <summary>
    /// The message envelope
    /// </summary>
    public required MessageEnvelope<TMessage> Envelope { get; init; }

    /// <summary>
    /// The topic or queue the message was received from
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>
    /// The consumer group (if applicable)
    /// </summary>
    public string? ConsumerGroup { get; init; }

    /// <summary>
    /// Delivery attempt count
    /// </summary>
    public int DeliveryCount { get; init; }

    /// <summary>
    /// Timestamp when the message was received
    /// </summary>
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Provider-specific metadata
    /// </summary>
    public IReadOnlyDictionary<string, object>? ProviderMetadata { get; init; }

    /// <summary>
    /// Cancellation token for the message processing
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
