using System;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>
/// Primary explicit API for enqueueing domain events transactionally with business state.
/// The caller invokes <see cref="EnqueueSourceGeneratedAsync{TPayload}"/> during a unit of work; the storage hook for
/// <typeparamref name="TContext"/> drains the buffer when the unit of work commits.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context (typically the caller's <c>DbContext</c>).</typeparam>
public interface IOutboxPublisher<TContext>
{
	/// <summary>
	/// Serializes <paramref name="payload"/> into an envelope and adds it to the outbox buffer.
	/// The envelope will be persisted when the <typeparamref name="TContext"/> unit of work commits.
	/// </summary>
	/// <remarks>
	/// This reflection-based overload is retained for compatibility. Application producers should use
	/// <see cref="EnqueueSourceGeneratedAsync{TPayload}"/> with producer-owned metadata.
	/// </remarks>
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

	/// <summary>
	/// Serializes <paramref name="payload"/> with producer-owned source-generated metadata and adds
	/// it to the outbox buffer. The envelope is persisted with the surrounding unit of work.
	/// </summary>
	/// <remarks>
	/// This additive default member preserves compatibility with custom publisher implementations
	/// while failing explicitly when they have not implemented the source-generated contract.
	/// </remarks>
	/// <typeparam name="TPayload">Concrete payload type represented by <paramref name="payloadTypeInfo"/>.</typeparam>
	/// <param name="topic">Logical topic / routing key the payload belongs to.</param>
	/// <param name="payload">Event payload. Must be non-null.</param>
	/// <param name="payloadTypeInfo">Producer-owned source-generated JSON metadata.</param>
	/// <param name="options">Optional per-call overrides (headers, dispatcher selection).</param>
	/// <param name="cancellationToken">Token to observe.</param>
	/// <returns>The <see cref="ReliableMessageEnvelope"/> created for this call.</returns>
	Task<ReliableMessageEnvelope> EnqueueSourceGeneratedAsync<TPayload>(
		string topic,
		TPayload payload,
		JsonTypeInfo<TPayload> payloadTypeInfo,
		OutboxPublishOverrides? options = null,
		CancellationToken cancellationToken = default)
		where TPayload : notnull
	{
		ArgumentNullException.ThrowIfNull(payloadTypeInfo);
		throw new NotSupportedException(
			$"Outbox publisher '{GetType().FullName}' does not support source-generated payload metadata.");
	}
}
