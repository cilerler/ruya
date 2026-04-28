using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>
/// Default <see cref="IOutboxPublisher{TContext}"/>: builds a <see cref="ReliableMessageEnvelope"/> and hands it to
/// the scoped <see cref="IOutboxBuffer{TContext}"/>. No I/O performed here; persistence happens when the
/// <typeparamref name="TContext"/>'s storage hook drains the buffer on commit.
/// </summary>
public sealed class OutboxPublisher<TContext> : IOutboxPublisher<TContext>
{
	private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

	private readonly IOutboxBuffer<TContext> _buffer;
	private readonly OutboxOptions _options;

	public OutboxPublisher(IOutboxBuffer<TContext> buffer, IOptions<ReliableMessagingOptions> options)
	{
		ArgumentNullException.ThrowIfNull(buffer);
		ArgumentNullException.ThrowIfNull(options);

		_buffer = buffer;
		_options = options.Value.Outbox;
	}

	public Task<ReliableMessageEnvelope> EnqueueAsync<TPayload>(
		string topic,
		TPayload payload,
		OutboxPublishOverrides? options = null,
		CancellationToken cancellationToken = default)
		where TPayload : notnull
	{
		ArgumentException.ThrowIfNullOrEmpty(topic);
		ArgumentNullException.ThrowIfNull(payload);

		cancellationToken.ThrowIfCancellationRequested();

		var payloadType = typeof(TPayload);
		var envelope = new ReliableMessageEnvelope
		{
			Topic = topic,
			DispatcherName = options?.DispatcherName ?? _options.DefaultDispatcherName,
			PayloadJson = JsonSerializer.Serialize(payload, payloadType, _serializerOptions),
			PayloadType = payloadType.AssemblyQualifiedName ?? payloadType.FullName ?? payloadType.Name,
			Headers = options?.Headers,
		};

		_buffer.Add(envelope);
		return Task.FromResult(envelope);
	}
}
