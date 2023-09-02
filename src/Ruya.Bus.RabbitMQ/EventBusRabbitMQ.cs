using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Ruya.Bus.Abstractions;
using Ruya.Bus.Events;
using Ruya.Bus.Extensions;

namespace Ruya.Bus.RabbitMQ;

// ReSharper disable once InconsistentNaming
public class EventBusRabbitMQ : IEventBus, IDisposable
{
	private const string ExchangeName = "eshop_event_bus";

	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger<EventBusRabbitMQ> _logger;

	private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };
	private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly IRabbitMQPersistentConnection _persistentConnection;
	private readonly int _retryCount;
	private readonly IEventBusSubscriptionsManager _subsManager;

	private IModel _consumerChannel;
	private string _queueName;

	public EventBusRabbitMQ(IServiceProvider serviceProvider, ILogger<EventBusRabbitMQ> logger, IEventBusSubscriptionsManager? subsManager, IRabbitMQPersistentConnection persistentConnection, string queueName = null, int retryCount = 5)
	{
		_serviceProvider = serviceProvider;
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
		_persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
		_queueName = queueName;
		_consumerChannel = CreateConsumerChannel();
		_retryCount = retryCount;
		_subsManager.OnEventRemoved += SubsManager_OnEventRemoved;
	}

	public void Dispose()
	{
		_consumerChannel.Dispose();
		_subsManager.Clear();
	}

	public void Publish(IntegrationEvent @event)
	{
		if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

		RetryPolicy? policy = Policy.Handle<BrokerUnreachableException>()
			.Or<SocketException>()
			.WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
			{
				_logger.LogWarning(ex, "Could not publish event: {EventId} after {Timeout}s", @event.Id, $"{time.TotalSeconds:n1}");
			});

		string eventName = @event.GetType().Name;

		_logger.LogTrace("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", @event.Id, eventName);

		using IModel channel = _persistentConnection.CreateModel();
		_logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);

		channel.ExchangeDeclare(ExchangeName, "direct");

		byte[] body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), IndentedOptions);

		policy.Execute(() =>
		{
			IBasicProperties? properties = channel.CreateBasicProperties();
			properties.DeliveryMode = 2; // persistent

			_logger.LogTrace("Publishing event to RabbitMQ: {EventId}", @event.Id);

			channel.BasicPublish(
				ExchangeName,
				eventName,
				true,
				properties,
				body);
		});
	}

	public void Subscribe<T, TH>()
		where T : IntegrationEvent
		where TH : IIntegrationEventHandler<T>
	{
		string eventName = _subsManager.GetEventKey<T>();
		DoInternalSubscription(eventName);

		_logger.LogInformation("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TH).GetGenericTypeName());

		_subsManager.AddSubscription<T, TH>();
		StartBasicConsume();
	}

	public void Unsubscribe<T, TH>()
		where T : IntegrationEvent
		where TH : IIntegrationEventHandler<T>
	{
		string eventName = _subsManager.GetEventKey<T>();

		_logger.LogInformation("Unsubscribing from event {EventName}", eventName);

		_subsManager.RemoveSubscription<T, TH>();
	}

	private void SubsManager_OnEventRemoved(object sender, string eventName)
	{
		if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

		using IModel channel = _persistentConnection.CreateModel();
		channel.QueueUnbind(_queueName,
			ExchangeName,
			eventName);

		if (!_subsManager.IsEmpty) return;

		_queueName = string.Empty;
		_consumerChannel.Close();
	}

	public void SubscribeDynamic<TH>(string eventName)
		where TH : IDynamicIntegrationEventHandler
	{
		_logger.LogInformation("Subscribing to dynamic event {EventName} with {EventHandler}", eventName, typeof(TH).GetGenericTypeName());

		DoInternalSubscription(eventName);
		_subsManager.AddDynamicSubscription<TH>(eventName);
		StartBasicConsume();
	}

	private void DoInternalSubscription(string eventName)
	{
		bool containsKey = _subsManager.HasSubscriptionsForEvent(eventName);
		if (containsKey) return;
		if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

		_consumerChannel.QueueBind(_queueName,
			ExchangeName,
			eventName);
	}

	public void UnsubscribeDynamic<TH>(string eventName)
		where TH : IDynamicIntegrationEventHandler
	{
		_subsManager.RemoveDynamicSubscription<TH>(eventName);
	}

	private void StartBasicConsume()
	{
		_logger.LogTrace("Starting RabbitMQ basic consume");

		if (_consumerChannel != null)
		{
			var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

			consumer.Received += ConsumerReceivedAsync;

			_consumerChannel.BasicConsume(
				_queueName,
				false,
				consumer);
		}
		else
		{
			_logger.LogError("StartBasicConsume can't call on _consumerChannel == null");
		}
	}

	private async Task ConsumerReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
	{
		string? eventName = eventArgs.RoutingKey;
		string message = Encoding.UTF8.GetString(eventArgs.Body.Span);

		try
		{
			if (message.ToLowerInvariant().Contains("throw-fake-exception"))
				throw new InvalidOperationException($"Fake exception requested: \"{message}\"");

			await ProcessEventAsync(eventName, message);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error Processing message \"{Message}\"", message);
		}

		// Even on exception we take the message off the queue.
		// in a REAL WORLD app this should be handled with a Dead Letter Exchange (DLX).
		// For more information see: https://www.rabbitmq.com/dlx.html
		_consumerChannel.BasicAck(eventArgs.DeliveryTag, false);
	}

	private IModel CreateConsumerChannel()
	{
		if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

		_logger.LogTrace("Creating RabbitMQ consumer channel");

		IModel channel = _persistentConnection.CreateModel();

		channel.ExchangeDeclare(ExchangeName,
			"direct");

		channel.QueueDeclare(_queueName,
			true,
			false,
			false,
			null);

		channel.CallbackException += (sender, ea) =>
		{
			_logger.LogWarning(ea.Exception, "Recreating RabbitMQ consumer channel");

			_consumerChannel.Dispose();
			_consumerChannel = CreateConsumerChannel();
			StartBasicConsume();
		};

		return channel;
	}

	private async Task ProcessEventAsync(string eventName, string message)
	{
		_logger.LogTrace("Processing RabbitMQ event: {EventName}", eventName);

		if (_subsManager.HasSubscriptionsForEvent(eventName))
		{
			await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
			IEnumerable<InMemoryEventBusSubscriptionsManager.SubscriptionInfo> subscriptions = _subsManager.GetHandlersForEvent(eventName);
			foreach (InMemoryEventBusSubscriptionsManager.SubscriptionInfo subscription in subscriptions)
				if (subscription.IsDynamic)
				{
					if (scope.ServiceProvider.GetService(subscription.HandlerType) is not IDynamicIntegrationEventHandler handler) continue;
					using dynamic eventData = JsonDocument.Parse(message);
					await Task.Yield();
					await handler.Handle(eventData);
				}
				else
				{
					object? handler = scope.ServiceProvider.GetService(subscription.HandlerType);
					if (handler == null) continue;
					Type eventType = _subsManager.GetEventTypeByName(eventName);
					object? integrationEvent = JsonSerializer.Deserialize(message, eventType, CaseInsensitiveOptions);
					Type concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

					await Task.Yield();
					await (Task)concreteType.GetMethod("Handle").Invoke(handler, new[] { integrationEvent });
				}
		}
		else
		{
			_logger.LogWarning("No subscription for RabbitMQ event: {EventName}", eventName);
		}
	}
}
