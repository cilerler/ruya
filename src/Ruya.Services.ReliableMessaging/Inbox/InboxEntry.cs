using System;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// Persistence record for an inbox dedup row.
/// PK is the composite <c>(ConsumerName, MessageId)</c>; insertion failure by uniqueness is the dedup signal.
/// </summary>
public sealed class InboxEntry
{
	/// <summary>Logical consumer identity. Part of the composite PK.</summary>
	public string ConsumerName { get; set; } = string.Empty;

	/// <summary>Inbound message id (matches <see cref="ReliableMessageEnvelope.MessageId"/>). Part of the composite PK.</summary>
	public string MessageId { get; set; } = string.Empty;

	public string Topic { get; set; } = string.Empty;

	public DateTime ReceivedAt { get; set; }

	public DateTime? ProcessedAt { get; set; }

	public InboxStatus Status { get; set; } = InboxStatus.Received;
}
