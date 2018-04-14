using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Ruya.EventBus.Abstractions;
using Ruya.EventBus.Events;

namespace Ruya.EventBus.RabbitMQ
{
    public class EventBusRabbitMQ : IEventBus, IDisposable
    {
        private const string BROKER_NAME = "event_bus";
        private readonly ILogger<EventBusRabbitMQ> _logger;

        private readonly IRabbitMQPersistentConnection _persistentConnection;
        private readonly int _retryCount;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEventBusSubscriptionsManager _subsManager;

        private IModel _consumerChannel;
        private string _queueName;

        public EventBusRabbitMQ(IRabbitMQPersistentConnection persistentConnection, ILogger<EventBusRabbitMQ> logger, IServiceProvider serviceProvider, IEventBusSubscriptionsManager subsManager, string queueName = null, int retryCount = 5)
        {
            _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
            _queueName = queueName;
            _consumerChannel = CreateConsumerChannel();
            _serviceProvider = serviceProvider;
            _retryCount = retryCount;
            _subsManager.OnEventRemoved += SubsManager_OnEventRemoved;
        }

        public void Dispose()
        {
            _consumerChannel?.Dispose();
            _subsManager.Clear();
        }

        public void Publish(IntegrationEvent @event)
        {
            if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

            // ReSharper disable once AccessToStaticMemberViaDerivedType
            RetryPolicy policy = RetryPolicy.Handle<BrokerUnreachableException>()
                                            .Or<SocketException>()
                                            .WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) => { _logger.LogWarning(ex.ToString()); });

            using (IModel channel = _persistentConnection.CreateModel())
            {
                string eventName = @event.GetType()
                                         .Name;

                channel.ExchangeDeclare(BROKER_NAME, "direct");

                string message = JsonConvert.SerializeObject(@event);
                var body = Encoding.UTF8.GetBytes(message);

                policy.Execute(() =>
                               {
                                   IBasicProperties properties = channel.CreateBasicProperties();
                                   properties.DeliveryMode = 2; // persistent

                                   channel.BasicPublish(BROKER_NAME, eventName, true, properties, body);
                               });
            }
        }

        public void SubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
        {
            DoInternalSubscription(eventName);
            _subsManager.AddDynamicSubscription<TH>(eventName);
        }

        public void Subscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
        {
            string eventName = _subsManager.GetEventKey<T>();
            DoInternalSubscription(eventName);
            _subsManager.AddSubscription<T, TH>();
        }

        public void Unsubscribe<T, TH>() where TH : IIntegrationEventHandler<T> where T : IntegrationEvent => _subsManager.RemoveSubscription<T, TH>();

        public void UnsubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler => _subsManager.RemoveDynamicSubscription<TH>(eventName);

        private void SubsManager_OnEventRemoved(object sender, string eventName)
        {
            if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

            using (IModel channel = _persistentConnection.CreateModel())
            {
                channel.QueueUnbind(_queueName, BROKER_NAME, eventName);

                if (!_subsManager.IsEmpty) return;
                _queueName = string.Empty;
                _consumerChannel.Close();
            }
        }

        private void DoInternalSubscription(string eventName)
        {
            bool containsKey = _subsManager.HasSubscriptionsForEvent(eventName);
            if (containsKey) return;
            if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

            using (IModel channel = _persistentConnection.CreateModel())
            {
                channel.QueueBind(_queueName, BROKER_NAME, eventName);
            }
        }

        private IModel CreateConsumerChannel()
        {
            if (!_persistentConnection.IsConnected) _persistentConnection.TryConnect();

            IModel channel = _persistentConnection.CreateModel();

            channel.ExchangeDeclare(BROKER_NAME, "direct");

            channel.QueueDeclare(_queueName, true, false, false, null);


            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (model, ea) =>
                                 {
                                     string eventName = ea.RoutingKey;
                                     string message = Encoding.UTF8.GetString(ea.Body);

                                     await ProcessEvent(eventName, message);

                                     channel.BasicAck(ea.DeliveryTag, false);
                                 };

            channel.BasicConsume(_queueName, false, consumer);

            channel.CallbackException += (sender, ea) =>
                                         {
                                             _consumerChannel.Dispose();
                                             _consumerChannel = CreateConsumerChannel();
                                         };

            return channel;
        }

        private async Task ProcessEvent(string eventName, string message)
        {
            if (_subsManager.HasSubscriptionsForEvent(eventName))
            {
                var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
                using (IServiceScope scope = scopeFactory.CreateScope())
                {
                    var subscriptions = _subsManager.GetHandlersForEvent(eventName);
                    foreach (InMemoryEventBusSubscriptionsManager.SubscriptionInfo subscription in subscriptions)
                        if (subscription.IsDynamic)
                        {
                            if (!(scope.ServiceProvider.GetService(subscription.HandlerType) is IDynamicIntegrationEventHandler handler))
                            {
                                throw new NotImplementedException();
                            }
                            dynamic eventData = JObject.Parse(message);
                            await handler.Handle(eventData);
                        }
                        else
                        {
                            Type eventType = _subsManager.GetEventTypeByName(eventName);
                            object integrationEvent = JsonConvert.DeserializeObject(message, eventType);
                            object handler = scope.ServiceProvider.GetService(subscription.HandlerType);
                            Type concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
                            await (Task)concreteType.GetMethod("Handle")
                                                    .Invoke(handler, new[] {integrationEvent});
                        }
                }
            }
        }
    }
}
