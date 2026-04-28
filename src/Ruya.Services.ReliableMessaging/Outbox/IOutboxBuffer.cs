using System.Collections.Generic;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>
/// Per-scope buffer of envelopes pending persistence. Populated by <see cref="IOutboxPublisher{TContext}"/> during
/// a unit of work and drained by the context's storage hook when the unit of work commits.
/// The type parameter <typeparamref name="TContext"/> distinguishes buffers for different persistence contexts
/// (for example, per-module <c>DbContext</c>) so each draining mechanism only sees its own entries.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context this buffer belongs to.</typeparam>
public interface IOutboxBuffer<TContext>
{
	/// <summary>Appends an envelope to the buffer.</summary>
	void Add(ReliableMessageEnvelope envelope);

	/// <summary>Returns the currently buffered envelopes and clears the buffer atomically.</summary>
	IReadOnlyList<ReliableMessageEnvelope> Drain();

	/// <summary>Count of envelopes currently in the buffer. Useful for diagnostics.</summary>
	int Count { get; }
}
