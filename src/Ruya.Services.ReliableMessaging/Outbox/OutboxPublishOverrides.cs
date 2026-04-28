using System.Collections.Generic;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>Optional per-call overrides passed to <see cref="IOutboxPublisher{TContext}.EnqueueAsync{TPayload}"/>.</summary>
public sealed class OutboxPublishOverrides
{
	/// <summary>
	/// Override <see cref="OutboxOptions.DefaultDispatcherName"/> for this single envelope.
	/// Interpreted by the active <see cref="IOutboundDispatcher"/> implementation.
	/// </summary>
	public string? DispatcherName { get; init; }

	/// <summary>Additional headers carried on the envelope.</summary>
	public IReadOnlyDictionary<string, string>? Headers { get; init; }
}
