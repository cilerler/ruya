﻿using System;
using RabbitMQ.Client;

namespace Ruya.Bus.RabbitMQ;

// ReSharper disable once InconsistentNaming
public interface IRabbitMQPersistentConnection : IDisposable
{
	bool IsConnected { get; }

	bool TryConnect();

	IModel CreateModel();
}
