using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace Ruya.Bus.ServiceBus;

public class DefaultServiceBusPersisterConnection : IServiceBusPersisterConnection
{
	private readonly string _serviceBusConnectionString;

	private bool _disposed;
	private ServiceBusClient _topicClient;

	public DefaultServiceBusPersisterConnection(string serviceBusConnectionString)
	{
		_serviceBusConnectionString = serviceBusConnectionString;
		AdministrationClient = new ServiceBusAdministrationClient(_serviceBusConnectionString);
		_topicClient = new ServiceBusClient(_serviceBusConnectionString);
	}

	public ServiceBusClient TopicClient
	{
		get
		{
			if (_topicClient.IsClosed) _topicClient = new ServiceBusClient(_serviceBusConnectionString);
			return _topicClient;
		}
	}

	public ServiceBusAdministrationClient AdministrationClient { get; }

	public ServiceBusClient CreateModel()
	{
		if (_topicClient.IsClosed) _topicClient = new ServiceBusClient(_serviceBusConnectionString);

		return _topicClient;
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;

		_disposed = true;
		await _topicClient.DisposeAsync();
	}
}
