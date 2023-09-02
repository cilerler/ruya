﻿using System;
using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Ruya.Bus.RabbitMQ;

// ReSharper disable once UnusedMember.Global
// ReSharper disable once InconsistentNaming
public class DefaultRabbitMQPersistentConnection : IRabbitMQPersistentConnection
{
	private readonly IConnectionFactory _connectionFactory;
	private readonly ILogger<DefaultRabbitMQPersistentConnection> _logger;
	private readonly int _retryCount;

	private readonly object _syncRoot = new();
	private IConnection _connection;
	public bool Disposed;

	public DefaultRabbitMQPersistentConnection(ILogger<DefaultRabbitMQPersistentConnection> logger, IConnectionFactory connectionFactory, int retryCount = 5)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
		_retryCount = retryCount;
	}

	public bool IsConnected => _connection is { IsOpen: true } && !Disposed;

	public IModel CreateModel()
	{
		if (!IsConnected) throw new InvalidOperationException("No RabbitMQ connections are available to perform this action");

		return _connection.CreateModel();
	}

	public void Dispose()
	{
		if (Disposed) return;

		Disposed = true;

		try
		{
			_connection.ConnectionShutdown -= OnConnectionShutdown;
			_connection.CallbackException -= OnCallbackException;
			_connection.ConnectionBlocked -= OnConnectionBlocked;
			_connection.Dispose();
		}
		catch (IOException ex)
		{
			_logger.LogCritical(ex.ToString());
		}
	}

	public bool TryConnect()
	{
		_logger.LogInformation("RabbitMQ Client is trying to connect");

		lock (_syncRoot)
		{
			RetryPolicy? policy = Policy.Handle<SocketException>()
				.Or<BrokerUnreachableException>()
				.WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
					{
						_logger.LogWarning(ex, "RabbitMQ Client could not connect after {TimeOut}s", $"{time.TotalSeconds:n1}");
					}
				);

			policy.Execute(() =>
			{
				_connection = _connectionFactory.CreateConnection();
			});

			if (IsConnected)
			{
				_connection.ConnectionShutdown += OnConnectionShutdown;
				_connection.CallbackException += OnCallbackException;
				_connection.ConnectionBlocked += OnConnectionBlocked;

				_logger.LogInformation("RabbitMQ Client acquired a persistent connection to '{HostName}' and is subscribed to failure events",
					_connection.Endpoint.HostName);

				return true;
			}

			_logger.LogCritical("Fatal error: RabbitMQ connections could not be created and opened");

			return false;
		}
	}

	private void OnConnectionBlocked(object sender, ConnectionBlockedEventArgs e)
	{
		if (Disposed) return;

		_logger.LogWarning("A RabbitMQ connection is shutdown. Trying to re-connect...");

		TryConnect();
	}

	private void OnCallbackException(object sender, CallbackExceptionEventArgs e)
	{
		if (Disposed) return;

		_logger.LogWarning("A RabbitMQ connection throw exception. Trying to re-connect...");

		TryConnect();
	}

	private void OnConnectionShutdown(object sender, ShutdownEventArgs reason)
	{
		if (Disposed) return;

		_logger.LogWarning("A RabbitMQ connection is on shutdown. Trying to re-connect...");

		TryConnect();
	}
}
