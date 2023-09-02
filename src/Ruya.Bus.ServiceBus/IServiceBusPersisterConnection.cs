using System;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace Ruya.Bus.ServiceBus;

public interface IServiceBusPersisterConnection : IAsyncDisposable
{
	ServiceBusClient TopicClient { get; }
	ServiceBusAdministrationClient AdministrationClient { get; }
}
