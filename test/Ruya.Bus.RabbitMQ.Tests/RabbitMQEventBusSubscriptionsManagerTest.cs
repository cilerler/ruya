using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Bus.Abstractions;

namespace Ruya.Bus.RabbitMQ.Tests;

[TestClass]
// ReSharper disable once InconsistentNaming
public class RabbitMQEventBusSubscriptionsManagerTest
{
	private static TestContext _testContext;
	private static IServiceProvider _serviceProvider;
	private static ILogger _logger;

	[ClassInitialize]
	public static void ClassInitialize(TestContext testContext)
	{
		_testContext = testContext;

		IServiceCollection serviceCollection = new ServiceCollection();
		using (IEnumerator<ServiceDescriptor> sc = Initialize.ServiceCollection.GetEnumerator())
		{
			while (sc.MoveNext()) serviceCollection.Add(sc.Current);
		}

		serviceCollection.AddEventBus();
		serviceCollection.AddEventBusRabbitMq(Initialize.ServiceProvider.GetRequiredService<IConfiguration>());

		serviceCollection.AddTransient<TestIntegrationEventHandler>();
		serviceCollection.AddTransient<TestIntegrationEvent>();

		_serviceProvider = serviceCollection.BuildServiceProvider();
		_logger = _serviceProvider.GetRequiredService<ILogger<RabbitMQEventBusSubscriptionsManagerTest>>();
		Task.Delay(TimeSpan.FromSeconds(1)).Wait();
	}

	[TestMethod]
	public void After_Creation_Should_Be_Empty()
	{
		_logger.LogInformation("Running a test... {MethodName}", nameof(After_Creation_Should_Be_Empty));

		IEventBus eventBus = _serviceProvider.GetRequiredService<IEventBus>();

		#region Publish

		// Exchange heros | Routing Key TestIntegrationEvent | Queue N/A
		eventBus.Publish(ActivatorUtilities.CreateInstance<TestIntegrationEvent>(_serviceProvider
			, 1), null);

		// Exchange heros | Routing Key hero | Queue hero.queue
		eventBus.Publish(ActivatorUtilities.CreateInstance<TestIntegrationEvent>(_serviceProvider
			, 2), new Dictionary<string, object> { { "routingKey", "hero" } });

		// Exchange heros | Routing Key hero | Queue hero.queue
		TestIntegrationEvent t1 = ActivatorUtilities.CreateInstance<TestIntegrationEvent>(_serviceProvider, 4);
		t1.Error = new List<object> { "Error message will be here" };
		eventBus.Publish(t1, new Dictionary<string, object> { { "routingKey", "hero" } });

		// Exchange heros | Routing Key hero | Queue hero.queue
		t1.PublishAsError = true;
		t1.TestId = 5;
		eventBus.Publish(t1, new Dictionary<string, object> { { "routingKey", "hero" } });

		// Exchange heros | Routing Key non-criminal | Queue hero.queue
		t1.TestId = 6;
		eventBus.Publish(t1, new Dictionary<string, object> { { "routingKey", "non-criminal" } });

		// Exchange heros | Routing Key supergirl| Queue N/A
		t1.TestId = 7;
		eventBus.Publish(t1, new Dictionary<string, object> { { "routingKey", "supergirl" } });

		// Exchange phantom-zone.DLX | Routing Key superman.villains | Queue daily-planet.queue.Errors
		t1.TestId = 8;
		eventBus.Publish(t1, new Dictionary<string, object> { { "routingKey", "supergirl" }, { "exchange", "superman" } });

		// Exchange phantom-zone.DLX | Routing Key superman.villains | Queue daily-planet.queue.Errors
		t1.TestId = 9;
		eventBus.Publish(t1, new Dictionary<string, object> { { "routingKey", "ClarkKent" }, { "exchange", "superman" } });

		// Exchange superman | RoutingKey ClarkKent | Queue daily-planet.queue
		t1.PublishAsError = false;
		t1.TestId = 10;
		eventBus.Publish(t1, new Dictionary<string, object> { { "routingKey", "ClarkKent" }, { "exchange", "superman" } });

		#endregion

		eventBus.Subscribe<TestIntegrationEvent, TestIntegrationEventHandler>();
		eventBus.AddConsumerChannel("daily-planet.queue");

		Task.Delay(TimeSpan.FromSeconds(10)).Wait();

		eventBus.RemoveConsumerChannel("daily-planet.queue");
		eventBus.Unsubscribe<TestIntegrationEvent, TestIntegrationEventHandler>();

		//UNDONE Below line
		//Assert.IsTrue(manager.IsEmpty);
	}
}
