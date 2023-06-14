using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ruya.Bus.Events;

namespace Ruya.Bus.Abstractions;

public class IntegrationEventHandler
{
	protected static IServiceProvider ServiceProvider;
	protected static ILogger Logger;

	public void PublishError(IntegrationEvent @event, Dictionary<string, object> parameters)
	{
		IServiceScopeFactory scopeFactory = ServiceProvider.GetRequiredService<IServiceScopeFactory>();
		using (IServiceScope scope = scopeFactory.CreateScope())
		{
			IEventBus eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
			@event.PublishAsError = true;
			eventBus.Publish(@event, parameters);
		}
	}
}
