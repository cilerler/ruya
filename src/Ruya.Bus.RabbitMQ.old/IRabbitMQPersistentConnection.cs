using System;
using RabbitMQ.Client;

namespace Ruya.Bus.RabbitMQ;

public interface IRabbitMQPersistentConnection 
: IDisposable
{
	bool IsConnected { get; }

	bool TryConnect();

	IModel CreateModel();
}
