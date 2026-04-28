using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.ReliableMessaging.MessageQueue;

/// <summary>
/// <see cref="IOutboundDispatcher"/> implementation that forwards outbox envelopes to
/// <see cref="IMessageQueue.PublishAsync{TMessage}"/> via <see cref="IMessageQueueFactory"/>.
/// </summary>
public sealed class MessageQueueOutboundDispatcher : IOutboundDispatcher
{
	private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
	private static readonly MethodInfo _publishAsyncMethod =
		typeof(IMessagePublisher).GetMethod(nameof(IMessagePublisher.PublishAsync))
		?? throw new InvalidOperationException("IMessagePublisher.PublishAsync method not found.");

	private static readonly ConcurrentDictionary<Type, MethodInfo> _publishMethodsByType = new();

	private readonly IMessageQueueFactory _factory;
	private readonly MessageQueueDispatcherOptions _options;

	public MessageQueueOutboundDispatcher(
		IMessageQueueFactory factory,
		IOptions<MessageQueueDispatcherOptions> options)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ArgumentNullException.ThrowIfNull(options);
		_factory = factory;
		_options = options.Value;
	}

	public async Task DispatchAsync(ReliableMessageEnvelope envelope, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(envelope);

		var queueName = string.IsNullOrWhiteSpace(envelope.DispatcherName)
			? _options.QueueName
			: envelope.DispatcherName;

		var payloadType = Type.GetType(envelope.PayloadType, throwOnError: true)
			?? throw new InvalidOperationException(
				$"Could not resolve payload type '{envelope.PayloadType}' for envelope '{envelope.MessageId}'.");

		var payload = JsonSerializer.Deserialize(envelope.PayloadJson, payloadType, _serializerOptions)
			?? throw new InvalidOperationException(
				$"Deserialized payload was null for envelope '{envelope.MessageId}'.");

		var publishOptions = BuildPublishOptions(envelope);

		var queue = await _factory.CreateQueueAsync(queueName, cancellationToken).ConfigureAwait(false);

		var method = _publishMethodsByType.GetOrAdd(payloadType, static t => _publishAsyncMethod.MakeGenericMethod(t));
		var task = (Task?)method.Invoke(queue, new[] { envelope.Topic, payload, publishOptions, (object)cancellationToken })
			?? throw new InvalidOperationException("IMessageQueue.PublishAsync returned null task.");
		await task.ConfigureAwait(false);
	}

	private static PublishOptions BuildPublishOptions(ReliableMessageEnvelope envelope)
	{
		var options = new PublishOptions();

		if (envelope.Headers is null || envelope.Headers.Count == 0)
		{
			return options;
		}

		foreach (var pair in envelope.Headers)
		{
			switch (pair.Key)
			{
				case "CorrelationId":
					options.CorrelationId = pair.Value;
					break;
				case "CausationId":
					options.CausationId = pair.Value;
					break;
				case "Source":
					options.Source = pair.Value;
					break;
				default:
					options.Headers ??= new System.Collections.Generic.Dictionary<string, object>(System.StringComparer.Ordinal);
					options.Headers[pair.Key] = pair.Value;
					break;
			}
		}

		return options;
	}
}
