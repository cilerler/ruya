using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.ReliableMessaging.Inbox;

namespace Ruya.Services.ReliableMessaging.MessageQueue;

/// <summary>
/// Consumer-side idempotency: wraps <see cref="IMessageSubscriber.SubscribeAsync{TMessage}"/> so that incoming messages
/// are dedup'd via <see cref="IInboxStore{TContext}"/> before the handler runs. Duplicates return
/// <see cref="MessageResult.Success"/> without invoking the handler.
/// </summary>
public static class MessageQueueSubscribeWithInboxExtensions
{
	/// <summary>
	/// Subscribes to <paramref name="topic"/> with consumer-side dedup using an explicit consumer name.
	/// </summary>
	public static Task<IMessageSubscription> SubscribeWithInboxAsync<TMessage, TDbContext>(
		this IMessageQueue queue,
		string topic,
		string consumerName,
		IServiceScopeFactory scopeFactory,
		Func<MessageContext<TMessage>, Task<MessageResult>> handler,
		SubscribeOptions? options = null,
		CancellationToken cancellationToken = default)
		where TMessage : class
	{
		ArgumentNullException.ThrowIfNull(queue);
		ArgumentException.ThrowIfNullOrEmpty(topic);
		ArgumentException.ThrowIfNullOrEmpty(consumerName);
		ArgumentNullException.ThrowIfNull(scopeFactory);
		ArgumentNullException.ThrowIfNull(handler);

		return queue.SubscribeAsync<TMessage>(
			topic,
			context => DedupAndHandleAsync<TMessage, TDbContext>(context, consumerName, topic, scopeFactory, handler),
			options,
			cancellationToken);
	}

	/// <summary>
	/// Subscribes to <paramref name="topic"/> with consumer-side dedup. The consumer name is resolved from
	/// <typeparamref name="THandlerMarker"/> via <see cref="IInboxConsumerNameProvider"/> (default: full type name,
	/// overridable via <see cref="InboxConsumerNameAttribute"/>).
	/// </summary>
	public static Task<IMessageSubscription> SubscribeWithInboxAsync<TMessage, TDbContext, THandlerMarker>(
		this IMessageQueue queue,
		string topic,
		IServiceScopeFactory scopeFactory,
		IInboxConsumerNameProvider consumerNameProvider,
		Func<MessageContext<TMessage>, Task<MessageResult>> handler,
		SubscribeOptions? options = null,
		CancellationToken cancellationToken = default)
		where TMessage : class
		where THandlerMarker : class
	{
		ArgumentNullException.ThrowIfNull(consumerNameProvider);

		var consumerName = consumerNameProvider.GetConsumerName(typeof(THandlerMarker));
		return queue.SubscribeWithInboxAsync<TMessage, TDbContext>(
			topic,
			consumerName,
			scopeFactory,
			handler,
			options,
			cancellationToken);
	}

	private static async Task<MessageResult> DedupAndHandleAsync<TMessage, TDbContext>(
		MessageContext<TMessage> context,
		string consumerName,
		string topic,
		IServiceScopeFactory scopeFactory,
		Func<MessageContext<TMessage>, Task<MessageResult>> handler)
		where TMessage : class
	{
		using var scope = scopeFactory.CreateScope();
		var store = scope.ServiceProvider.GetRequiredService<IInboxStore<TDbContext>>();

		var messageId = context.Envelope.MessageId;
		var ct = context.CancellationToken;

		var recorded = await store.TryRecordAsync(consumerName, messageId, topic, ct).ConfigureAwait(false);
		if (!recorded)
		{
			return MessageResult.Success(); // duplicate; skip handler
		}

		var result = await handler(context).ConfigureAwait(false);
		if (result.Status == MessageStatus.Success)
		{
			await store.MarkProcessedAsync(consumerName, messageId, ct).ConfigureAwait(false);
		}

		return result;
	}
}
