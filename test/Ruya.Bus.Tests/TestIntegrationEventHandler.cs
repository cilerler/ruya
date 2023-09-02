using System.Threading.Tasks;
using Ruya.Bus.Abstractions;

namespace Ruya.Bus.Tests;

public class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
	public bool Handled { get; private set; }
	public TestIntegrationEventHandler()
	{
		Handled = false;
	}

	public Task Handle(TestIntegrationEvent @event)
	{
		Handled = true;
		return Task.CompletedTask;
	}
}
