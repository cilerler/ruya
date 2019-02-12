using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Ruya.Bus.RabbitMQ
{
	public class DefaultRabbitMqPersistentConnection : IRabbitMqPersistentConnection
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly ILogger<DefaultRabbitMqPersistentConnection> _logger;
		
        private readonly BusSetting _options;

        private readonly object _syncRoot = new object();
        private IConnection _connection;
		
        public DefaultRabbitMqPersistentConnection(ILogger<DefaultRabbitMqPersistentConnection> logger, IOptions<BusSetting> options)
	    {
		    _logger = logger;
		    _options = options.Value;
		    _connectionFactory = new ConnectionFactory
		                         {
			                         HostName = _options.Connection
			                       , UserName = _options.UserName
			                       , Password = _options.Password
			                       , VirtualHost = _options.VirtualHost
			                       , AutomaticRecoveryEnabled = _options.AutomaticRecoveryEnabled
			                       , ClientProperties = new Dictionary<string, object>
			                                            {
				                                            {"Product", _options.AppId}
			                                            }
		                         };

		    if (_options.RequestedHeartbeatSeconds != default)
		    {
			    _connectionFactory.RequestedHeartbeat = (ushort)_options.RequestedHeartbeatSeconds.TotalSeconds;
		    }

		    try
		    {
			    EnsureDeclarations();
			}
			catch (Exception e)
			{
				_logger.LogCritical(e, "RabbitMQ declarations are experiencing issue.");
				throw;
			}
	    }

        public bool IsConnected => _connection?.IsOpen == true;

        public IModel CreateModel()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("No RabbitMQ connections are available to perform this action");
            }
            return _connection.CreateModel();
        }

		#region Dispose
		public void Dispose()
        {
	        Dispose(true);
	        GC.SuppressFinalize(this);
        }

		//x private IntPtr nativeResource = Marshal.AllocHGlobal(100);

		//x ~DefaultRabbitMqPersistentConnection()
		//x {
		//x   Dispose(false);
		//x }

		protected virtual void Dispose(bool disposing)
        {
            try
            {
	            _logger.LogDebug("Disposing existed connection of RabbitMQ");
				if (disposing)
				{
					_logger.LogDebug("Disposing managed resources");

					if (_connection == null)
		            {
			            return;
		            }

		            _connection.Dispose();
					_connection = null;
				}

				_logger.LogDebug("Disposing natıve resources");

				//x if (nativeResource != IntPtr.Zero)
				//x {
				//x  Marshal.FreeHGlobal(nativeResource);
				//x  nativeResource = IntPtr.Zero;
				//x }
			}
			catch (IOException ex)
            {
                _logger.LogCritical(ex.ToString());
            }
        }
		#endregion
		
		public void EnsureDeclarations()
		{
			if (!_options.EnableDeclarations)
			{
				return;
			}

			if (!IsConnected) TryConnect();
			using (IModel channel = CreateModel())
			{
				foreach (Exchange exchange in _options.Exchanges)
				{
					_logger.LogDebug("Creating exchange {Exchange}"
					                     , exchange.Name);
					channel.ExchangeDeclare(exchange.Name
					                      , exchange.Type
					                      , exchange.Durable
					                      , exchange.AutoDelete
					                      , exchange.Arguments);
				}

				foreach (Queue queue in _options.Queues)
				{
					(string dlx, string dlk, string dlq, bool dlExists) = DeadLetterHelper.GetValues(queue);
					if (dlExists)
					{						
						_logger.LogDebug("Declaring dead letter objects {Exchange} {RoutingKey} {Queue}"
						                     , dlx, dlk, dlq);

						channel.ExchangeDeclare(dlx
						                      , DeadLetterHelper.DeadLetterExchangeType
						                      , queue.Durable
						                      , queue.AutoDelete
						                      , null);

						channel.QueueDeclare(queue: dlq
						                   , durable: true
						                   , exclusive: false
						                   , autoDelete: false);

						channel.QueueBind(queue: dlq
						                , exchange: dlx
						                , routingKey: dlk);
					}

					_logger.LogDebug("Creating queue {Queue}"
					                     , queue.Name);
					channel.QueueDeclare(queue: queue.Name
					                   , durable: queue.Durable
					                   , exclusive: queue.Exclusive
					                   , autoDelete: queue.AutoDelete
					                   , arguments: queue.Arguments);
				}

				foreach (Binding binding in _options.Bindings)
				{
					channel.QueueBind(queue: binding.Destination
					                , exchange: binding.Source
					                , routingKey: binding.RoutingKey);
				}
			}
		}

		public bool TryConnect()
        {
            lock (_syncRoot)
            {
				//https://github.com/App-vNext/Polly.Extensions.Http
                RetryPolicy policy = Policy.Handle<SocketException>()
                                           .Or<BrokerUnreachableException>()
                                           .WaitAndRetry(_options.RetryCount
                                                       , retryAttempt =>
                                                         {
                                                             _logger.LogDebug("RabbitMQ Client is retrying to connect. Attempt {retryAttempt}", retryAttempt);
                                                             return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                                         }
                                                       , (ex, time) => { _logger.LogWarning(ex.ToString()); });
                policy.Execute(() =>
                               {
                                   _logger.LogInformation("RabbitMQ Client is trying to connect {MessageQueueServer}", _connectionFactory.Uri);
                                   _connection = _connectionFactory.CreateConnection();
                               });

                if (IsConnected)
                {
                    _connection.ConnectionShutdown += OnConnectionShutdown;
                    _connection.CallbackException += OnCallbackException;
                    _connection.ConnectionBlocked += OnConnectionBlocked;
                    _logger.LogInformation($"RabbitMQ persistent connection acquired a connection {_connection.Endpoint.HostName} and is subscribed to failure events");
                    return true;
                }

                _logger.LogCritical("FATAL ERROR: RabbitMQ connections could not be created and opened");
                return false;
            }
        }

        private void OnConnectionBlocked(object sender, ConnectionBlockedEventArgs e)
        {
            _logger.LogWarning("A RabbitMQ connection is shutdown. Trying to re-connect...");
            TryConnect();
        }

        private void OnCallbackException(object sender, CallbackExceptionEventArgs e)
        {
            _logger.LogWarning("A RabbitMQ connection throw exception. Trying to re-connect...");
            TryConnect();
        }

        private void OnConnectionShutdown(object sender, ShutdownEventArgs reason)
        {
            _logger.LogWarning("A RabbitMQ connection is on shutdown. Trying to re-connect...");
            TryConnect();
        }
    }
}
