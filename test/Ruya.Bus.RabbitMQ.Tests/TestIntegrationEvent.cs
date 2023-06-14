using System;
using Microsoft.Extensions.Logging;
using Ruya.Bus.Events;

namespace Ruya.Bus.RabbitMQ.Tests;

// Integration Events notes:
// An Event is “something that has happened in the past”, therefore its name has to be
// An Integration Event is an event that can cause side effects to other micro services, Bounded-Contexts or external systems.
public class TestIntegrationEvent : IntegrationEvent
{
	private static IServiceProvider _serviceProvider;
	private static ILogger _logger;

	public TestIntegrationEvent(IServiceProvider serviceProvider, ILogger<TestIntegrationEvent> logger, int testId)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
		TestId = testId;
	}

	public int TestId { set; get; }
}
