using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Ruya.Bus.Abstractions;
using Ruya.Bus.Events;

namespace Ruya.Bus.RabbitMQ;

// ReSharper disable once InconsistentNaming
public sealed class EventBusRabbitMq : IEventBus, IDisposable
{
	private const string ExchangeWord = "exchange";
	private const string RoutingKeyWord = "routingKey";
	private readonly ILogger<EventBusRabbitMq> _logger;
	private readonly BusSetting _options;

	private readonly IRabbitMqPersistentConnection _persistentConnection;
	private readonly IServiceProvider _serviceProvider;
	private readonly IEventBusSubscriptionsManager _subscriptionsManager;
	private IModel _consumerChannel;
	private Dictionary<string, IModel> _consumerChannels = new();

	// ReSharper disable once SuggestBaseTypeForParameter
	public EventBusRabbitMq(IServiceProvider serviceProvider, ILogger<EventBusRabbitMq> logger, IOptions<BusSetting> options,
		IRabbitMqPersistentConnection persistentConnection, IEventBusSubscriptionsManager subscriptionsManager)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
		_options = options.Value;

		_persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
		_subscriptionsManager = subscriptionsManager ?? _serviceProvider.GetRequiredService<InMemoryEventBusSubscriptionsManager>();

		//_consumerChannel = CreateConsumerChannel();
		_subscriptionsManager.OnEventRemoved += SubsManager_OnEventRemoved;
	}

	public void Dispose()
	{
		foreach (KeyValuePair<string, IModel> consumerChannel in _consumerChannels)
		{
			if (consumerChannel.Value == null) continue;

			consumerChannel.Value.Close();
			consumerChannel.Value.Dispose();
		}

		//_consumerChannel?.Dispose();
		_subscriptionsManager.Clear();
	}

	public void Publish(IntegrationEvent @event, Dictionary<string, object> parameters)
	{
		string eventType = @event.GetType().Name;
		( string exchange, string routingKey ) = PublishHelper(eventType
			, @event.PublishAsError
			, parameters);

		if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

		// ReSharper disable once AccessToStaticMemberViaDerivedType
		RetryPolicy policy = RetryPolicy.Handle<BrokerUnreachableException>()
			.Or<SocketException>()
			.WaitAndRetry(_options.RetryCount
				, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2
					, retryAttempt))
				, (ex, time) => { _logger.LogWarning(ex.ToString()); });

		using (IModel channel = _persistentConnection.CreateModel())
		{
			string message = JsonSerializer.Serialize(@event, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
			byte[] body = Encoding.UTF8.GetBytes(message);
			channel.BasicAcks += BasicAcks;
			channel.BasicReturn += BasicReturn;
			channel.ConfirmSelect();

			policy.Execute(() =>
			{
				IBasicProperties basicProperties = channel.CreateBasicProperties();
				basicProperties.Persistent = true;
				basicProperties.ContentType = "application/json"; //MediaTypeNames.Application.Json;
				basicProperties.Type = eventType;
				basicProperties.AppId = _options.AppId;
				basicProperties.UserId = _options.UserName;
				_logger.LogDebug("Publishing {EventType} message to exchange {Exchange} with {RoutingKey} as {Body}"
					, eventType
					, exchange
					, routingKey
					, message);
				channel.BasicPublish(exchange
					, routingKey
					, true
					, basicProperties
					, body);
				if (_options.WaitForConfirmsOrDieExists) channel.WaitForConfirmsOrDie(_options.WaitForConfirmsOrDie);
			});
		}
	}

	public void SubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
	{
		_subscriptionsManager.AddDynamicSubscription<TH>(eventName);
		//DoInternalSubscription(eventName);
	}

	public void Subscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
	{
		_subscriptionsManager.AddSubscription<T, TH>();
		//string eventName = _subscriptionsManager.GetEventKey<T>();
		//DoInternalSubscription(eventName);
	}

	public void Unsubscribe<T, TH>() where TH : IIntegrationEventHandler<T> where T : IntegrationEvent
	{
		_subscriptionsManager.RemoveSubscription<T, TH>();
	}

	public void UnsubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
	{
		_subscriptionsManager.RemoveDynamicSubscription<TH>(eventName);
	}

	public void AddConsumerChannel(string queueName)
	{
		if (_consumerChannels.ContainsKey(queueName))
		{
			_logger.LogWarning("Consumer for queue {queueName} does already exist.");
			return;
		}

		_consumerChannels.Add($"{queueName}_{Guid.NewGuid()}", CreateConsumerChannel(queueName));
	}

	public void RemoveConsumerChannel(string queueName)
	{
		if (!_consumerChannels.ContainsKey(queueName))
		{
			_logger.LogWarning("Consumer for queue {queueName} does not exist.");
			return;
		}

		_consumerChannels[queueName]
			.Close();
		_consumerChannels[queueName]
			.Dispose();
		_consumerChannels.Remove(queueName);
	}

	private (string Exchange, string RoutingKey) PublishHelper(string eventType, bool publishAsError, Dictionary<string, object> parameters)
	{
		string exchange = _options.BrokerName;
		string routingKey = eventType;
		if (parameters == null) return ( exchange, routingKey );

		if (parameters.ContainsKey(ExchangeWord)) exchange = parameters[ExchangeWord].ToString();

		if (parameters.ContainsKey(RoutingKeyWord)) routingKey = parameters[RoutingKeyWord].ToString();

		if (!publishAsError) return ( exchange, routingKey );

		var queueNamesFromBindings = _options.Bindings
			.Where(b => b.Source.Equals(exchange, StringComparison.CurrentCultureIgnoreCase) &&
			            b.RoutingKey.Equals(routingKey, StringComparison.CurrentCultureIgnoreCase)).Select(b => b.Destination).ToList();
		if (!queueNamesFromBindings.Any())
		{
			_logger.LogWarning("There should be at least one binding with given exchange and routing key to identify DLX and DLK.");
			return ( exchange, routingKey );
		}

		if (queueNamesFromBindings.Count() > 1)
			// TODO implement a way to handle multiple bindings, for now it will pick the first one
			_logger.LogWarning("There are more than one queue identified with this exchange and routingKey.  Will use first one");

		Queue queue = _options.Queues.FirstOrDefault(q => queueNamesFromBindings.Contains(q.Name)
		                                                  && DeadLetterHelper.GetValues(q).DeadLetterExists);
		// ReSharper disable once InvertIf
		if (queue == null)
		{
			_logger.LogWarning("Identified queue {QueueNamesFromBindings} doesn't have DLX or DLK, can't identify any error routingKey",
				queueNamesFromBindings);
			return ( exchange, routingKey );
		}

		( string dlx, string dlk, string dlq, bool dlExists ) = DeadLetterHelper.GetValues(queue);
		// ReSharper disable once InvertIf
		if (dlExists)
		{
			exchange = dlx;
			routingKey = dlk;
		}

		return ( exchange, routingKey );
	}

	private void BasicReturn(object sender, BasicReturnEventArgs e)
	{
		byte[] body = e.Body.ToArray();
		string message = Encoding.UTF8.GetString(body);
		var parameters = new Dictionary<string, object> { { "exchange", e.Exchange }, { "routingKey", e.RoutingKey } };
		_logger.LogError(
			"BasicReturn message has been received from RabbitMQ. Session {@Sender} BasicReturnEventArgs {ReplyText} {ReplyCode} {EventName} {@Parameters} {Message}"
			, sender.ToString()
			, e.ReplyText
			, e.ReplyCode
			, e.BasicProperties.Type
			, parameters
			, message);
	}

	private void BasicAcks(object sender, BasicAckEventArgs e)
	{
		_logger.LogDebug("BasicAcks message has been received by RabbitMQ.  Session {@Sender} BasicAckEventArgs {@BasicAckEventArgs}",
			sender.ToString(), e);
	}

	private void SubsManager_OnEventRemoved(object sender, string eventName)
	{
		//x UnbindQueue(eventName);

		if (_subscriptionsManager.IsEmpty)
			//_options.SubscriptionClientName = string.Empty;
			//_consumerChannel.Close();
			//_consumerChannel.Dispose();
			RemoveAllConsumerChannels();
	}

	private void RemoveAllConsumerChannels()
	{
		foreach (KeyValuePair<string, IModel> consumerChannel in _consumerChannels)
		{
			if (consumerChannel.Value == null) continue;

			consumerChannel.Value.Close();
			consumerChannel.Value.Dispose();
		}

		_consumerChannels = new Dictionary<string, IModel>();
	}

	private void DoInternalSubscription(string eventName)
	{
		//if (_consumerChannel==null)//(_subscriptionsManager.IsEmpty)
		//{
		//	_consumerChannel = CreateConsumerChannel();
		//}

		bool containsKey = _subscriptionsManager.HasSubscriptionsForEvent(eventName);
		if (containsKey) return;

		//x BindQueue(eventName);
	}

	//private void BindQueue(string routingKey)
	//{
	//	if (!_persistentConnection.IsConnected)
	//	{
	//		_persistentConnection.TryConnect();
	//	}

	//	using (IModel channel = _persistentConnection.CreateModel())
	//	{
	//		channel.QueueBind(queue: _options.SubscriptionClientName
	//						, exchange: _options.BrokerName
	//						, routingKey: routingKey);
	//	}
	//}

	//private void UnbindQueue(string routingKey)
	//{
	//	if (!_persistentConnection.IsConnected)
	//	{
	//		_persistentConnection.TryConnect();
	//	}

	//	using (IModel channel = _persistentConnection.CreateModel())
	//	{
	//		channel.QueueUnbind(queue: _options.SubscriptionClientName
	//						, exchange: _options.BrokerName
	//						, routingKey: routingKey);
	//	}
	//}

	private IModel CreateConsumerChannel(string queueName)
	{
		if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

		IModel channel = _persistentConnection.CreateModel();

		var consumer = new EventingBasicConsumer(channel); // DefaultBasicConsumer
		consumer.Received += async (sender, basicDeliverEventArgs) =>
		{
			ReadOnlyMemory<byte> body = basicDeliverEventArgs.Body;
			string message = Encoding.UTF8.GetString(body.ToArray());
			string eventName = basicDeliverEventArgs.BasicProperties.Type;
			var parameters = new Dictionary<string, object>
			{
				{ "exchange", basicDeliverEventArgs.Exchange }, { "routingKey", basicDeliverEventArgs.RoutingKey }
			};

			_logger.LogDebug(
				"BasicDelivery message has been received from RabbitMQ. BasicDeliverEventArgs {ReDelivered} {EventName} {@Parameters} {Message}"
				, basicDeliverEventArgs.Redelivered
				, eventName
				, parameters
				, message);

			try
			{
				await ProcessEventAsync(eventName, message, parameters);
				channel.BasicAck(basicDeliverEventArgs.DeliveryTag
					, false);
			}
			catch (NotSupportedException)
			{
				channel.BasicNack(basicDeliverEventArgs.DeliveryTag
					, false
					, false);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, ex.Message);
				channel.BasicNack(basicDeliverEventArgs.DeliveryTag
					, false
					, false);
				throw;
			}
		};

		channel.BasicQos(0, _options.PrefetchCount, false);
		channel.BasicConsume(queueName //_options.SubscriptionClientName
			, false
			, consumer);

		channel.CallbackException += (sender, ea) =>
		{
			_consumerChannel.Dispose();
			_consumerChannel = CreateConsumerChannel(queueName);
		};
		return channel;
	}

	private async Task ProcessEventAsync(string eventName, string message, Dictionary<string, object> parameters)
	{
		if (_subscriptionsManager.HasSubscriptionsForEvent(eventName))
		{
			IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
			using (IServiceScope scope = scopeFactory.CreateScope())
			{
				IEnumerable<SubscriptionInfo> subscriptions = _subscriptionsManager.GetHandlersForEvent(eventName);
				foreach (SubscriptionInfo subscription in subscriptions)
					if (subscription.IsDynamic)
					{
						if (!( scope.ServiceProvider.GetService(subscription.HandlerType) is IDynamicIntegrationEventHandler handler )) continue;

						JsonDocument document = JsonDocument.Parse(message);
						dynamic eventData = document.RootElement;
						await handler.Handle(eventData);
					}
					else
					{
						object handler = scope.ServiceProvider.GetService(subscription.HandlerType);
						if (handler == null) continue;

						Type eventType = _subscriptionsManager.GetEventTypeByName(eventName);
						object integrationEvent = JsonSerializer.Deserialize(message, eventType);
						Type concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
						if (!concreteType.IsInstanceOfType(handler))
						{
							_logger.LogWarning(
								"Handler doesn't implement `IIntegrationEventHandler<>` EventName {eventName} Message {message} Subscription {@subscription}",
								eventName, message, subscription);
							throw new NotImplementedException("Handler doesn't implement IIntegrationEventHandler<>");
						}

						//object instance = ActivatorUtilities.CreateInstance(scope.ServiceProvider, concreteType);
						await (Task)concreteType //instance.GetType()
							.GetMethod("Handle")
							.Invoke(handler
								, new[] { integrationEvent, parameters });
					}
			}
		}
		else
		{
			_logger.LogWarning("Received a payload for event {EventName} but there is no subscription exists. The payload is {Message}", eventName,
				message);
			throw new NotSupportedException("No subscription exists to process incoming data!");
		}
	}
}
