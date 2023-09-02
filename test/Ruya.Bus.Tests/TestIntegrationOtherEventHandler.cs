using System.Collections.Generic;
using System.Threading.Tasks;
using Ruya.Bus.Abstractions;

namespace Ruya.Bus.Tests;

public class TestIntegrationOtherEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
	public bool Handled { get; private set; }

	public TestIntegrationOtherEventHandler()
	{
		Handled = false;
	}


	public Task Handle(TestIntegrationEvent @event)
	{
		Handled = true;
		return Task.CompletedTask;
	}
}
