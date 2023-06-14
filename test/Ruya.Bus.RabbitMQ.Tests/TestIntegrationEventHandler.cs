using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ruya.Bus.Abstractions;

namespace Ruya.Bus.RabbitMQ.Tests;

public class TestIntegrationEventHandler : IntegrationEventHandler, IIntegrationEventHandler<TestIntegrationEvent>
{
	public TestIntegrationEventHandler(IServiceProvider serviceProvider, ILogger<TestIntegrationEventHandler> logger)
	{
		ServiceProvider = serviceProvider;
		Logger = logger;
		Handled = false;
	}

	public bool Handled { get; private set; }

	public async Task Handle(TestIntegrationEvent @event, Dictionary<string, object> parameters)
	{
		Handled = true;
		Logger.LogDebug("{@payload}"
			, @event);
		if (@event.TestId % 10 == 0)
		{
			if (@event.Error == null) @event.Error = new List<object>();
			@event.Error.Add(@event.Error.Any()
				? $"Adding Error {@event.Error.Count + 1}"
				: "Initial");

			PublishError(@event, parameters);
		}

		await Task.CompletedTask;
	}
}
