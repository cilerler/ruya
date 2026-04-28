using System;
using System.Collections.Generic;

namespace Ruya.Services.ReliableMessaging;

/// <summary>
/// Persistence-ready envelope for a reliably delivered message.
/// Produced by <see cref="Outbox.IOutboxPublisher{TContext}"/>, persisted by <see cref="Outbox.IOutboxStore{TContext}"/>,
/// and consumed by implementations of <see cref="IOutboundDispatcher"/>.
/// </summary>
public sealed record ReliableMessageEnvelope
{
	/// <summary>Unique identifier for the message; also serves as the idempotency key on the consumer side.</summary>
	public Guid MessageId { get; init; } = Guid.NewGuid();

	/// <summary>Logical topic / routing key the payload belongs to.</summary>
	public string Topic { get; init; } = string.Empty;

	/// <summary>
	/// Optional name of a specific outbound dispatcher. When <see langword="null"/>, <see cref="IOutboundDispatcher"/> implementations
	/// should route via their own default. Interpreted by dispatcher adapters (e.g. named MessageQueue provider).
	/// </summary>
	public string? DispatcherName { get; init; }

	/// <summary>Serialized payload.</summary>
	public string PayloadJson { get; init; } = string.Empty;

	/// <summary>Assembly-qualified name of the payload type, used for deserialization.</summary>
	public string PayloadType { get; init; } = string.Empty;

	/// <summary>Optional headers to carry alongside the payload (e.g. correlation id, causation id, source).</summary>
	public IReadOnlyDictionary<string, string>? Headers { get; init; }

	/// <summary>When the envelope was enqueued by the producer.</summary>
	public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}
