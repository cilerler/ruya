using System;
using System.Collections;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.RabbitMq
{
    public class Client : IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;

        private EventHandler<MessageQueueEventArgs> _afterInitialSettings;

        public IModel Channel;

        // ReSharper disable once SuggestBaseTypeForParameter
        public Client(IConfiguration configuration, ILogger<Client> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public IMessageQueueSettings Configuration { get; set; }

        private IConnection Connection { set; get; }

        #region IDisposable Members

        public void Dispose()
        {
            _logger.LogTrace("Disposing existed Channel and Connection of RabbitMQ");
            if (Channel != null)
            {
                if (Channel.IsOpen)
                {
                    Channel.Close();
                }
                Channel.Dispose();
            }
            // ReSharper disable once InvertIf
            if (Connection != null)
            {
                if (Connection.IsOpen)
                {
                    Connection.Close();
                }
                Connection.Dispose();
            }
        }

        #endregion

        public Client SetConfiguration(IMessageQueueSettings settings)
        {
            Configuration = settings;
            return this;
        }

        public bool GetCounts(out uint messageCount, out uint consumerCount)
        {
            messageCount = 0;
            consumerCount = 0;
            if (!EnsureConnection())
            {
                return false;
            }
            bool errorOccured = false;
            try
            {
                _logger.LogTrace("Retrieving counts from RabbitMQ");
                QueueDeclareOk output = Channel.QueueDeclare(Configuration.Queue, true, false, false);
                messageCount = output.MessageCount;
                consumerCount = output.ConsumerCount;
            }
            catch (Exception ex)
            {
                errorOccured = true;
                _logger.LogError(-1, ex, $"There is something wrong with RabbitMQ connection. {ex.Message}");
                Channel = null;
            }
            return !errorOccured;
        }

        public bool Publish(string message)
        {
            if (!EnsureConnection())
            {
                return false;
            }
            bool errorOccured = false;

            byte[] body = Encoding.UTF8.GetBytes(message);
            try
            {
                IBasicProperties basicProperties = Channel.CreateBasicProperties();
                basicProperties.Persistent = true;
                basicProperties.ContentType = "application/json";

				_logger.LogInformation($"Sending message to RabbitMQ to {Configuration.RoutingKey}");
                Channel.BasicPublish(Configuration.Exchange, Configuration.RoutingKey, basicProperties, body);
                Channel.WaitForConfirmsOrDie();
                _logger.LogTrace("Message sent to RabbitMQ");
            }
            catch (Exception ex)
            {
                errorOccured = true;
                _logger.LogError(-1, ex, $"There is something wrong with RabbitMQ connection. {ex.Message}");
                Channel = null;
            }
            return !errorOccured;
        }

        public bool Subscribe()
        {
            if (!EnsureConnection())
            {
                return false;
            }
            bool errorOccured = false;
            var consumer = new EventingBasicConsumer(Channel); // DefaultBasicConsumer
			consumer.Received += OnConsumerOnReceived;
            Channel.BasicConsume(Configuration.Queue, false, consumer);

            //while (!unsubscribe())
            //{
            //    if (!Channel.IsOpen || Channel.IsClosed)
            //        throw new Exception("The message broker connection or channel was closed unexpectedly.");
            //    BasicDeliverEventArgs deliverEventArgs = consumer.Queue.Dequeue();
            //    try
            //    {
            //        if (acknowledge(deliverEventArgs.Body))
            //            Channel.BasicAck(deliverEventArgs.DeliveryTag, false);
            //    }
            //    catch (Exception ex)
            //    {
            //        if (reject(ex))
            //            Channel.BasicReject(deliverEventArgs.DeliveryTag, true);
            //        else
            //            Channel.BasicAck(deliverEventArgs.DeliveryTag, false);
            //    }
            //}
            return !errorOccured;
        }

        private void OnConsumerOnReceived(object model, BasicDeliverEventArgs basicDeliverEventArgs)
        {
            byte[] body = basicDeliverEventArgs.Body;
            string message = Encoding.UTF8.GetString(body);
            //x Channel.BasicAck(deliveryTag: basicDeliverEventArgs.DeliveryTag, multiple: false);
            //x Channel.BasicReject(basicDeliverEventArgs.DeliveryTag, requeue: true);
            _logger.LogTrace("Invoking registered events received from RabbitMQ");
            var parameters = new MessageQueueEventArgs
                             {
                                 Message = message,
                                 DeliveryTag = basicDeliverEventArgs.DeliveryTag
                             };
            _afterInitialSettings?.Invoke(this, parameters);
            //x _afterInitialSettings?.Invoke(this, EventArgs.Empty);
        }

        private bool EnsureConnection()
        {
            bool connectionExist = Connection != null && Connection.IsOpen;
            bool channelExist = Channel != null && Channel.IsOpen;

            if (connectionExist && channelExist)
            {
                _logger.LogTrace("Connection and Channel are exist for RabbitMQ");
                return true;
            }

            if (!connectionExist)
            {
                _logger.LogTrace("There is no connection detected for RabbitMQ");
            }

            if (!channelExist)
            {
                _logger.LogTrace("There is no channel detected for RabbitMQ");
            }

            Dispose();

            bool output;
            try
            {
                string url = _configuration.GetConnectionString(Configuration.ConnectionStringKey);
                var connectionFactory = new ConnectionFactory
                                        {
                                            Uri = new Uri(url)
                                        };
                if (Configuration.AutomaticRecoveryEnabled)
                {
                    connectionFactory.AutomaticRecoveryEnabled = Configuration.AutomaticRecoveryEnabled;
                }

                if (Configuration.RequestedHeartbeatSeconds != default)
                {
                    connectionFactory.RequestedHeartbeat = (ushort)Configuration.RequestedHeartbeatSeconds.TotalSeconds;
                }

                _logger.LogTrace($"Initializing RabbitMQ connection to {connectionFactory.HostName}:{connectionFactory.Port}");

                Connection = connectionFactory.CreateConnection();
                Channel = Connection.CreateModel();
                Channel.ExchangeDeclare(exchange: Configuration.Exchange, type: Configuration.ExchangeType, durable:true, autoDelete:false, arguments: null);
                Channel.QueueDeclare(queue:Configuration.Queue, durable:true, exclusive:false, autoDelete: false, arguments: null);
                Channel.ConfirmSelect();
                Channel.QueueBind(queue:Configuration.Queue, exchange:Configuration.Exchange, routingKey: Configuration.RoutingKey);
                Channel.BasicQos(prefetchSize: 0, prefetchCount: Configuration.PrefetchCount, global:false);
                _logger.LogTrace("RabbitMQ connection established.");
                output = true;
            }
            catch (Exception ex)
            {
                output = false;
                _logger.LogError(-1, ex, $"There is something wrong with RabbitMQ connection. {ex.Message}");
                Channel = null;
            }
            return output;
        }

        public event EventHandler<MessageQueueEventArgs> AfterInitialSettings
        {
            add
            {
                bool existInInvocationList = _afterInitialSettings != null && !((IList)_afterInitialSettings.GetInvocationList()).Contains(value);
                if (!existInInvocationList)
                {
                    _afterInitialSettings += value;
                }
            }
            remove
            {
                bool existInInvocationList = _afterInitialSettings != null && ((IList)_afterInitialSettings.GetInvocationList()).Contains(value);
                if (existInInvocationList)
                {
                    // ReSharper disable once DelegateSubtraction
                    _afterInitialSettings -= value;
                }
            }
        }

        public void MessageAck(ulong deliveryTag, bool multiple)
        {
            _logger.LogTrace("Sending acknowledge to RabbitMQ");
            Channel.BasicAck(deliveryTag, multiple);
            // TODO implement exception handling here
            _logger.LogTrace("Acknowledge sent to RabbitMQ");
        }

        public void MessageReject(ulong deliveryTag, bool requeue)
        {
            _logger.LogTrace("Sending reject to RabbitMQ");
            Channel.BasicReject(deliveryTag, requeue);
            // TODO implement exception queue here
            _logger.LogTrace("Reject sent to RabbitMQ");
        }
    }
}
