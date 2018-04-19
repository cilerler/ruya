using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private const string ExchangeType = "direct";
        private readonly ILogger<EventBusRabbitMQ> _logger;
        private readonly EventBusSetting _options;

        private readonly IRabbitMQPersistentConnection _persistentConnection;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEventBusSubscriptionsManager _subsManager;

        private IModel _consumerChannel;

        // ReSharper disable once SuggestBaseTypeForParameter
        public EventBusRabbitMQ(IServiceProvider serviceProvider, ILogger<EventBusRabbitMQ> logger, IOptionsSnapshot<EventBusSetting> options, IRabbitMQPersistentConnection persistentConnection, IEventBusSubscriptionsManager subsManager)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;

            _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();

            _consumerChannel = CreateConsumerChannel();
            _subsManager.OnEventRemoved += SubsManager_OnEventRemoved;
        }

        public void Dispose()
        {
            _consumerChannel?.Dispose();
            _subsManager.Clear();
        }

        public void GetCounts(out uint messageCount, out uint consumerCount)
        {
            messageCount = 0;
            consumerCount = 0;
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }
            // ReSharper disable once AccessToStaticMemberViaDerivedType
            RetryPolicy policy = RetryPolicy.Handle<BrokerUnreachableException>()
                                            .Or<SocketException>()
                                            .WaitAndRetry(_options.RetryCount
                                                        , retryAttempt => TimeSpan.FromSeconds(Math.Pow(2
                                                                                                      , retryAttempt))
                                                        , (ex, time) => _logger.LogWarning(ex.ToString()));

            uint internalMessageCount = default(uint);
            uint internalConsumerCount = default(uint);
            using (IModel channel = _persistentConnection.CreateModel())
            {
                policy.Execute(() =>
                               {
                                   _logger.LogTrace("Retrieving counts from RabbitMQ");
                                   QueueDeclareOk output = channel.QueueDeclare(_options.SubscriptionClientName
                                                                              , true
                                                                              , false
                                                                              , false);
                                   internalMessageCount = output.MessageCount;
                                   internalConsumerCount = output.ConsumerCount;
                               });
            }
            messageCount = internalMessageCount;
            messageCount = internalConsumerCount;
        }

        public void Publish(IntegrationEvent @event)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            // ReSharper disable once AccessToStaticMemberViaDerivedType
            RetryPolicy policy = RetryPolicy.Handle<BrokerUnreachableException>().Or<SocketException>().WaitAndRetry(_options.RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) => { _logger.LogWarning(ex.ToString()); });

            using (IModel channel = _persistentConnection.CreateModel())
            {
                string eventName = @event.GetType().Name;
                channel.ExchangeDeclare(_options.BrokerName, ExchangeType);
                channel.ConfirmSelect();

                string message = JsonConvert.SerializeObject(@event);
                var body = Encoding.UTF8.GetBytes(message);
                
                policy.Execute(() =>
                               {
                                   IBasicProperties basicProperties = channel.CreateBasicProperties();
                                   basicProperties.Persistent = true;
                                   basicProperties.ContentType = "application/json";

                                   channel.BasicPublish(_options.BrokerName, eventName, true, basicProperties, body);
                                   if (_options.WaitForConfirmsOrDieExists)
                                   { 
                                        channel.WaitForConfirmsOrDie(_options.WaitForConfirmsOrDie);
                                   }
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
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            using (IModel channel = _persistentConnection.CreateModel())
            {
                channel.QueueUnbind(_options.SubscriptionClientName, _options.BrokerName, eventName);

                if (!_subsManager.IsEmpty)
                {
                    return;
                }

                _options.SubscriptionClientName = string.Empty;
                _consumerChannel.Close();
            }
        }

        private void DoInternalSubscription(string eventName)
        {
            bool containsKey = _subsManager.HasSubscriptionsForEvent(eventName);
            if (containsKey)
            {
                return;
            }

            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            using (IModel channel = _persistentConnection.CreateModel())
            {
                channel.QueueBind(_options.SubscriptionClientName, _options.BrokerName, eventName);
            }
        }

        private IModel CreateConsumerChannel()
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            IModel channel = _persistentConnection.CreateModel();
            channel.ExchangeDeclare(_options.BrokerName, ExchangeType);
            channel.QueueDeclare(_options.SubscriptionClientName, true, false, false, null);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (model, basicDeliverEventArgs) =>
                                 {
                                     string eventName = basicDeliverEventArgs.RoutingKey;
                                     var body = basicDeliverEventArgs.Body;
                                     string message = Encoding.UTF8.GetString(body);

                                     await ProcessEvent(eventName, message);

                                     channel.BasicAck(basicDeliverEventArgs.DeliveryTag, false);
                                     //x channel.BasicReject(basicDeliverEventArgs.DeliveryTag, requeue: true);
                                 };

            channel.BasicQos(0, _options.PrefetchCount, false);
            channel.BasicConsume(_options.SubscriptionClientName, false, consumer);

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
                    {
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
                            await (Task)concreteType.GetMethod("Handle").Invoke(handler, new[] {integrationEvent});
                        }
                    }
                }
            }
        }
    }
}
