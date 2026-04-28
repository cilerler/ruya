using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>
/// Primary explicit API for enqueueing domain events transactionally with business state.
/// The caller invokes <see cref="EnqueueAsync{TPayload}"/> during a unit of work; the storage hook for
/// <typeparamref name="TContext"/> drains the buffer when the unit of work commits.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context (typically the caller's <c>DbContext</c>).</typeparam>
public interface IOutboxPublisher<TContext>
{
	/// <summary>
	/// Serializes <paramref name="payload"/> into an envelope and adds it to the outbox buffer.
	/// The envelope will be persisted when the <typeparamref name="TContext"/> unit of work commits.
	/// </summary>
	/// <typeparam name="TPayload">Concrete payload type; serialized with the default JSON serializer.</typeparam>
	/// <param name="topic">Logical topic / routing key the payload belongs to.</param>
	/// <param name="payload">Event payload. Must be non-null.</param>
	/// <param name="options">Optional per-call overrides (headers, dispatcher selection).</param>
	/// <param name="cancellationToken">Token to observe.</param>
	/// <returns>The <see cref="ReliableMessageEnvelope"/> created for this call (useful for correlation).</returns>
	Task<ReliableMessageEnvelope> EnqueueAsync<TPayload>(
		string topic,
		TPayload payload,
		OutboxPublishOverrides? options = null,
		CancellationToken cancellationToken = default)
		where TPayload : notnull;
}
