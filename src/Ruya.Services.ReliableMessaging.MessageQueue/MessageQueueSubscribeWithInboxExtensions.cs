using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.ReliableMessaging.Inbox;

namespace Ruya.Services.ReliableMessaging.MessageQueue;

/// <summary>
/// Consumer-side idempotency: wraps <see cref="IMessageSubscriber"/> subscriptions so that incoming messages
/// and their business mutations execute atomically via <see cref="IAtomicInboxStore{TContext}"/>. Duplicates return
/// <see cref="MessageResult.Success"/> without invoking the handler. Non-success results roll back the inbox claim so
/// an intentional retry or later dead-letter replay can invoke the handler again.
/// </summary>
public static class MessageQueueSubscribeWithInboxExtensions
{
	// No concrete observer type exists; retain only that virtual logger-category suffix as a literal.
	private const string LoggerCategoryName =
		$"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.ReliableMessaging)}.{nameof(Ruya.Services.ReliableMessaging.MessageQueue)}.InboxPostCommitObserver";

	private static readonly Action<ILogger, string, string, string, Exception?> _logPostCommitObserverFailed =
		LoggerMessage.Define<string, string, string>(
			LogLevel.Error,
			new EventId(8101, "InboxPostCommitObserverFailed"),
			"Inbox post-commit observer failed for message '{MessageId}' on topic '{Topic}' and consumer '{ConsumerName}'. The committed delivery remains successful.");

	/// <summary>
	/// Subscribes to <paramref name="topic"/> with consumer-side dedup using an explicit consumer name.
	/// </summary>
	/// <remarks>
	/// This compatibility overload cannot supply the transaction-owning service scope to the handler. Prefer the overload
	/// whose handler receives an <see cref="IServiceProvider"/> so business services use the same scope as the inbox store.
	/// </remarks>
	[Obsolete("Use the overload whose handler accepts IServiceProvider so inbox and business work share one transaction-owning scope.")]
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
		ArgumentNullException.ThrowIfNull(handler);

		return queue.SubscribeWithInboxAsync<TMessage, TDbContext>(
			topic,
			consumerName,
			scopeFactory,
			(_, context) => handler(context),
			options,
			cancellationToken);
	}

	/// <summary>
	/// Subscribes to <paramref name="topic"/> with atomic consumer-side processing using an explicit consumer name.
	/// The handler must resolve scoped business services from the supplied <see cref="IServiceProvider"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="MessageStatus.Success"/> commits the Inbox row and enlisted business changes.
	/// <see cref="MessageStatus.Retry"/> and <see cref="MessageStatus.Reject"/> roll them back and preserve the handler's
	/// broker-facing result. Exceptions roll back and propagate. External side effects require independent idempotency.
	/// </remarks>
	public static Task<IMessageSubscription> SubscribeWithInboxAsync<TMessage, TDbContext>(
		this IMessageQueue queue,
		string topic,
		string consumerName,
		IServiceScopeFactory scopeFactory,
		Func<IServiceProvider, MessageContext<TMessage>, Task<MessageResult>> handler,
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
			context => ExecuteAndHandleAsync<TMessage, TDbContext>(context, consumerName, topic, scopeFactory, handler, null),
			options,
			cancellationToken);
	}

	/// <summary>
	/// Subscribes to <paramref name="topic"/> with consumer-side dedup. The consumer name is resolved from
	/// <typeparamref name="THandlerMarker"/> via <see cref="IInboxConsumerNameProvider"/> (default: full type name,
	/// overridable via <see cref="InboxConsumerNameAttribute"/>).
	/// </summary>
	/// <remarks>
	/// This compatibility overload cannot supply the transaction-owning service scope to the handler. Prefer the overload
	/// whose handler receives an <see cref="IServiceProvider"/> so business services use the same scope as the inbox store.
	/// </remarks>
	[Obsolete("Use the overload whose handler accepts IServiceProvider so inbox and business work share one transaction-owning scope.")]
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
		ArgumentNullException.ThrowIfNull(handler);

		var consumerName = consumerNameProvider.GetConsumerName(typeof(THandlerMarker));
		return queue.SubscribeWithInboxAsync<TMessage, TDbContext>(
			topic,
			consumerName,
			scopeFactory,
			(_, context) => handler(context),
			options,
			cancellationToken);
	}

	/// <summary>
	/// Subscribes to <paramref name="topic"/> with atomic consumer-side processing. The consumer name is resolved from
	/// <typeparamref name="THandlerMarker"/> via <see cref="IInboxConsumerNameProvider"/> (default: full type name,
	/// overridable via <see cref="InboxConsumerNameAttribute"/>). The handler must resolve scoped business services from
	/// the supplied <see cref="IServiceProvider"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="MessageStatus.Success"/> commits the Inbox row and enlisted business changes.
	/// <see cref="MessageStatus.Retry"/> and <see cref="MessageStatus.Reject"/> roll them back and preserve the handler's
	/// broker-facing result. Exceptions roll back and propagate. External side effects require independent idempotency.
	/// </remarks>
	public static Task<IMessageSubscription> SubscribeWithInboxAsync<TMessage, TDbContext, THandlerMarker>(
		this IMessageQueue queue,
		string topic,
		IServiceScopeFactory scopeFactory,
		IInboxConsumerNameProvider consumerNameProvider,
		Func<IServiceProvider, MessageContext<TMessage>, Task<MessageResult>> handler,
		SubscribeOptions? options = null,
		CancellationToken cancellationToken = default)
		where TMessage : class
		where THandlerMarker : class
	{
		ArgumentNullException.ThrowIfNull(consumerNameProvider);
		ArgumentNullException.ThrowIfNull(handler);

		var consumerName = consumerNameProvider.GetConsumerName(typeof(THandlerMarker));
		return queue.SubscribeWithInboxAsync<TMessage, TDbContext>(
			topic,
			consumerName,
			scopeFactory,
			handler,
			options,
			cancellationToken);
	}

	/// <summary>
	/// Subscribes to <paramref name="topic"/> with atomic consumer-side processing and invokes
	/// <paramref name="postCommitObserver"/> after a newly processed delivery commits.
	/// </summary>
	/// <remarks>
	/// The observer is best-effort and runs once only when the inbox store reports
	/// <see cref="InboxExecutionResult.Processed"/>. It does not run for a duplicate, retry, reject, or handler
	/// exception. Observer failures are logged and do not change the committed broker-facing success result.
	/// Use a transactional Outbox instead when the post-commit effect must be durable.
	/// </remarks>
	public static Task<IMessageSubscription> SubscribeWithInboxAndPostCommitAsync<TMessage, TDbContext>(
		this IMessageQueue queue,
		string topic,
		string consumerName,
		IServiceScopeFactory scopeFactory,
		Func<IServiceProvider, MessageContext<TMessage>, Task<MessageResult>> handler,
		Func<IServiceProvider, MessageContext<TMessage>, Task> postCommitObserver,
		SubscribeOptions? options = null,
		CancellationToken cancellationToken = default)
		where TMessage : class
	{
		ArgumentNullException.ThrowIfNull(queue);
		ArgumentException.ThrowIfNullOrEmpty(topic);
		ArgumentException.ThrowIfNullOrEmpty(consumerName);
		ArgumentNullException.ThrowIfNull(scopeFactory);
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentNullException.ThrowIfNull(postCommitObserver);

		return queue.SubscribeAsync<TMessage>(
			topic,
			context => ExecuteAndHandleAsync<TMessage, TDbContext>(
				context,
				consumerName,
				topic,
				scopeFactory,
				handler,
				postCommitObserver),
			options,
			cancellationToken);
	}

	/// <summary>
	/// Subscribes to <paramref name="topic"/> with atomic consumer-side processing and invokes
	/// <paramref name="postCommitObserver"/> after a newly processed delivery commits. The consumer name is resolved
	/// from <typeparamref name="THandlerMarker"/> via <see cref="IInboxConsumerNameProvider"/>.
	/// </summary>
	/// <remarks>
	/// The observer is best-effort and runs once only when the inbox store reports
	/// <see cref="InboxExecutionResult.Processed"/>. It does not run for a duplicate, retry, reject, or handler
	/// exception. Observer failures are logged and do not change the committed broker-facing success result.
	/// Use a transactional Outbox instead when the post-commit effect must be durable.
	/// </remarks>
	public static Task<IMessageSubscription> SubscribeWithInboxAndPostCommitAsync<TMessage, TDbContext, THandlerMarker>(
		this IMessageQueue queue,
		string topic,
		IServiceScopeFactory scopeFactory,
		IInboxConsumerNameProvider consumerNameProvider,
		Func<IServiceProvider, MessageContext<TMessage>, Task<MessageResult>> handler,
		Func<IServiceProvider, MessageContext<TMessage>, Task> postCommitObserver,
		SubscribeOptions? options = null,
		CancellationToken cancellationToken = default)
		where TMessage : class
		where THandlerMarker : class
	{
		ArgumentNullException.ThrowIfNull(consumerNameProvider);
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentNullException.ThrowIfNull(postCommitObserver);

		var consumerName = consumerNameProvider.GetConsumerName(typeof(THandlerMarker));
		return queue.SubscribeWithInboxAndPostCommitAsync<TMessage, TDbContext>(
			topic,
			consumerName,
			scopeFactory,
			handler,
			postCommitObserver,
			options,
			cancellationToken);
	}

	private static async Task<MessageResult> ExecuteAndHandleAsync<TMessage, TDbContext>(
		MessageContext<TMessage> context,
		string consumerName,
		string topic,
		IServiceScopeFactory scopeFactory,
		Func<IServiceProvider, MessageContext<TMessage>, Task<MessageResult>> handler,
		Func<IServiceProvider, MessageContext<TMessage>, Task>? postCommitObserver)
		where TMessage : class
	{
		await using var scope = scopeFactory.CreateAsyncScope();
		var store = scope.ServiceProvider.GetRequiredService<IAtomicInboxStore<TDbContext>>();
		var postCommitLogger = postCommitObserver is null
			? NullLogger.Instance
			: scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategoryName) ?? NullLogger.Instance;

		var messageId = context.Envelope.MessageId;
		var ct = context.CancellationToken;
		MessageResult? handlerResult = null;

		var executionResult = await store.ExecuteOnceAsync(
			consumerName,
			messageId,
			topic,
			async _ =>
			{
				handlerResult = await handler(scope.ServiceProvider, context).ConfigureAwait(false)
					?? throw new InvalidOperationException("The inbox handler returned null.");

				return handlerResult.Status == MessageStatus.Success
					? InboxWorkResult.Processed
					: InboxWorkResult.Abandoned;
			},
			ct).ConfigureAwait(false);

		var result = executionResult switch
		{
			InboxExecutionResult.Duplicate => MessageResult.Success(),
			InboxExecutionResult.Processed or InboxExecutionResult.Abandoned => handlerResult
				?? throw new InvalidOperationException("The inbox store completed without invoking the handler."),
			_ => throw new InvalidOperationException($"Unsupported inbox execution result '{executionResult}'."),
		};

		if (executionResult == InboxExecutionResult.Processed && postCommitObserver is not null)
		{
			await NotifyPostCommitAsync(
				scope.ServiceProvider,
				context,
				consumerName,
				topic,
				messageId,
				postCommitLogger,
				postCommitObserver).ConfigureAwait(false);
		}

		return result;
	}

	[SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "Observer failures happen after commit and must never make the broker retry committed work.")]
	private static async Task NotifyPostCommitAsync<TMessage>(
		IServiceProvider services,
		MessageContext<TMessage> context,
		string consumerName,
		string topic,
		string messageId,
		ILogger logger,
		Func<IServiceProvider, MessageContext<TMessage>, Task> postCommitObserver)
		where TMessage : class
	{
		try
		{
			await postCommitObserver(services, context).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_logPostCommitObserverFailed(logger, messageId, topic, consumerName, exception);
		}
	}
}
