using System;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>
/// Persistence record for an outbox row. Stored in the caller's database alongside business state
/// (typically a table named <c>{Module}.Outbox</c>).
/// </summary>
public sealed class OutboxEntry
{
	/// <summary>Primary key; matches <see cref="ReliableMessageEnvelope.MessageId"/>.</summary>
	public Guid Id { get; set; }

	public string Topic { get; set; } = string.Empty;

	public string? DispatcherName { get; set; }

	public string PayloadJson { get; set; } = string.Empty;

	public string PayloadType { get; set; } = string.Empty;

	/// <summary>Serialized (JSON) headers dictionary, or <see langword="null"/>.</summary>
	public string? HeadersJson { get; set; }

	public DateTime EnqueuedAt { get; set; }

	public DateTime? DispatchedAt { get; set; }

	/// <summary>Scheduled time at which the processor may attempt dispatch again.</summary>
	public DateTime NextAttemptAt { get; set; }

	public int AttemptCount { get; set; }

	public string? LastError { get; set; }

	public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
}
