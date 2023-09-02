using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ruya.Bus.Abstractions;
using Ruya.Bus.Events;

namespace Ruya.Bus.ServiceBus;

public class EventBusServiceBus : IEventBus, IAsyncDisposable
{
	private const string INTEGRATION_EVENT_SUFFIX = "IntegrationEvent";
	private readonly ILogger<EventBusServiceBus> _logger;
	private readonly ServiceBusProcessor _processor;
	private readonly ServiceBusSender _sender;
	private readonly IServiceBusPersisterConnection _serviceBusPersisterConnection;
	private readonly IServiceProvider _serviceProvider;
	private readonly string _subscriptionName;
	private readonly IEventBusSubscriptionsManager _subsManager;
	private readonly string _topicName = "eshop_event_bus";

	public EventBusServiceBus(IServiceProvider serviceProvider, ILogger<EventBusServiceBus> logger, IEventBusSubscriptionsManager? subsManager, IServiceBusPersisterConnection serviceBusPersisterConnection, string subscriptionClientName)
	{
		_serviceProvider = serviceProvider;
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
		_serviceBusPersisterConnection = serviceBusPersisterConnection;
		_subscriptionName = subscriptionClientName;
		_sender = _serviceBusPersisterConnection.TopicClient.CreateSender(_topicName);
		var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 10, AutoCompleteMessages = false };
		_processor = _serviceBusPersisterConnection.TopicClient.CreateProcessor(_topicName, _subscriptionName, options);

		RemoveDefaultRule();
		RegisterSubscriptionClientMessageHandlerAsync().GetAwaiter().GetResult();
	}

	public async ValueTask DisposeAsync()
	{
		_subsManager.Clear();
		await _processor.CloseAsync();
	}

	public void Publish(IntegrationEvent @event)
	{
		string eventName = @event.GetType().Name.Replace(INTEGRATION_EVENT_SUFFIX, "");
		string jsonMessage = JsonSerializer.Serialize(@event, @event.GetType());
		byte[] body = Encoding.UTF8.GetBytes(jsonMessage);

		var message = new ServiceBusMessage { MessageId = Guid.NewGuid().ToString(), Body = new BinaryData(body), Subject = eventName };

		_sender.SendMessageAsync(message)
			.GetAwaiter()
			.GetResult();
	}

	public void Subscribe<T, TH>()
		where T : IntegrationEvent
		where TH : IIntegrationEventHandler<T>
	{
		string eventName = typeof(T).Name.Replace(INTEGRATION_EVENT_SUFFIX, "");

		bool containsKey = _subsManager.HasSubscriptionsForEvent<T>();
		if (!containsKey)
			try
			{
				_serviceBusPersisterConnection.AdministrationClient.CreateRuleAsync(_topicName, _subscriptionName,
						new CreateRuleOptions { Filter = new CorrelationRuleFilter { Subject = eventName }, Name = eventName }).GetAwaiter()
					.GetResult();
			}
			catch (ServiceBusException)
			{
				_logger.LogWarning("The messaging entity {eventName} already exists.", eventName);
			}

		_logger.LogInformation("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TH).Name);

		_subsManager.AddSubscription<T, TH>();
	}

	public void Unsubscribe<T, TH>()
		where T : IntegrationEvent
		where TH : IIntegrationEventHandler<T>
	{
		string eventName = typeof(T).Name.Replace(INTEGRATION_EVENT_SUFFIX, "");

		try
		{
			_serviceBusPersisterConnection
				.AdministrationClient
				.DeleteRuleAsync(_topicName, _subscriptionName, eventName)
				.GetAwaiter()
				.GetResult();
		}
		catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
		{
			_logger.LogWarning("The messaging entity {eventName} Could not be found.", eventName);
		}

		_logger.LogInformation("Unsubscribing from event {EventName}", eventName);

		_subsManager.RemoveSubscription<T, TH>();
	}

	public void SubscribeDynamic<TH>(string eventName)
		where TH : IDynamicIntegrationEventHandler
	{
		_logger.LogInformation("Subscribing to dynamic event {EventName} with {EventHandler}", eventName, typeof(TH).Name);

		_subsManager.AddDynamicSubscription<TH>(eventName);
	}

	public void UnsubscribeDynamic<TH>(string eventName)
		where TH : IDynamicIntegrationEventHandler
	{
		_logger.LogInformation("Unsubscribing from dynamic event {EventName}", eventName);

		_subsManager.RemoveDynamicSubscription<TH>(eventName);
	}

	private async Task RegisterSubscriptionClientMessageHandlerAsync()
	{
		_processor.ProcessMessageAsync +=
			async args =>
			{
				var eventName = $"{args.Message.Subject}{INTEGRATION_EVENT_SUFFIX}";
				var messageData = args.Message.Body.ToString();

				// Complete the message so that it is not received again.
				if (await ProcessEvent(eventName, messageData)) await args.CompleteMessageAsync(args.Message);
			};

		_processor.ProcessErrorAsync += ErrorHandler;
		await _processor.StartProcessingAsync();
	}

	private Task ErrorHandler(ProcessErrorEventArgs args)
	{
		Exception? ex = args.Exception;
		ServiceBusErrorSource context = args.ErrorSource;

		_logger.LogError(ex, "Error handling message - Context: {@ExceptionContext}", context);

		return Task.CompletedTask;
	}

	private async Task<bool> ProcessEvent(string eventName, string message)
	{
		var processed = false;
		if (_subsManager.HasSubscriptionsForEvent(eventName))
		{
			await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
			IEnumerable<InMemoryEventBusSubscriptionsManager.SubscriptionInfo> subscriptions = _subsManager.GetHandlersForEvent(eventName);
			foreach (InMemoryEventBusSubscriptionsManager.SubscriptionInfo subscription in subscriptions)
				if (subscription.IsDynamic)
				{
					if (scope.ServiceProvider.GetService(subscription.HandlerType) is not IDynamicIntegrationEventHandler handler) continue;

					using dynamic eventData = JsonDocument.Parse(message);
					await handler.Handle(eventData);
				}
				else
				{
					object? handler = scope.ServiceProvider.GetService(subscription.HandlerType);
					if (handler == null) continue;
					Type eventType = _subsManager.GetEventTypeByName(eventName);
					object? integrationEvent = JsonSerializer.Deserialize(message, eventType);
					Type concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
					await (Task)concreteType.GetMethod("Handle").Invoke(handler, new[] { integrationEvent });
				}
		}

		processed = true;
		return processed;
	}

	private void RemoveDefaultRule()
	{
		try
		{
			_serviceBusPersisterConnection
				.AdministrationClient
				.DeleteRuleAsync(_topicName, _subscriptionName, RuleProperties.DefaultRuleName)
				.GetAwaiter()
				.GetResult();
		}
		catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
		{
			_logger.LogWarning("The messaging entity {DefaultRuleName} Could not be found.", RuleProperties.DefaultRuleName);
		}
	}
}
