using System.Collections.Generic;
using System.Threading.Tasks;
using Ruya.Bus.Abstractions;

namespace Ruya.Bus.Tests;

public class TestIntegrationOtherEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
	public TestIntegrationOtherEventHandler()
	{
		Handled = false;
	}

	public bool Handled { get; private set; }

	public async Task Handle(TestIntegrationEvent @event, Dictionary<string, object> parameters)
	{
		Handled = true;
		await Task.CompletedTask;
	}
}
