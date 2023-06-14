using System;
using RabbitMQ.Client;

namespace Ruya.Bus.RabbitMQ;

public interface IRabbitMqPersistentConnection : IDisposable
{
	bool IsConnected { get; }

	bool TryConnect();

	IModel CreateModel();
}
