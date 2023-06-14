using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Bus.Tests;

[TestClass]
public class InMemoryEventBusSubscriptionsManagerTest
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

		serviceCollection.AddTransient<TestIntegrationEventHandler>();
		serviceCollection.AddTransient<TestIntegrationEvent>();

		_serviceProvider = serviceCollection.BuildServiceProvider();
		_logger = _serviceProvider.GetRequiredService<ILogger<InMemoryEventBusSubscriptionsManagerTest>>();
	}

	[TestMethod]
	public void After_Creation_Should_Be_Empty()
	{
		InMemoryEventBusSubscriptionsManager? manager = _serviceProvider.GetService<InMemoryEventBusSubscriptionsManager>();
		Assert.IsTrue(manager.IsEmpty);
	}

	[TestMethod]
	public void After_One_Event_Subscription_Should_Contain_The_Event()
	{
		InMemoryEventBusSubscriptionsManager? manager = _serviceProvider.GetService<InMemoryEventBusSubscriptionsManager>();
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		Assert.IsTrue(manager.HasSubscriptionsForEvent<TestIntegrationEvent>());
	}

	[TestMethod]
	public void After_All_Subscriptions_Are_Deleted_Event_Should_No_Longer_Exists()
	{
		InMemoryEventBusSubscriptionsManager? manager = _serviceProvider.GetService<InMemoryEventBusSubscriptionsManager>();
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		manager.RemoveSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		Assert.IsFalse(manager.HasSubscriptionsForEvent<TestIntegrationEvent>());
	}

	[TestMethod]
	public void Deleting_Last_Subscription_Should_Raise_On_Deleted_Event()
	{
		var raised = false;
		InMemoryEventBusSubscriptionsManager? manager = _serviceProvider.GetService<InMemoryEventBusSubscriptionsManager>();
		manager.OnEventRemoved += (o, e) => raised = true;
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		manager.RemoveSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		Assert.IsTrue(raised);
	}

	[TestMethod]
	public void Get_Handlers_For_Event_Should_Return_All_Handlers()
	{
		InMemoryEventBusSubscriptionsManager? manager = _serviceProvider.GetService<InMemoryEventBusSubscriptionsManager>();
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationOtherEventHandler>();
		IEnumerable<SubscriptionInfo> handlers = manager.GetHandlersForEvent<TestIntegrationEvent>();
		Assert.AreEqual(2, handlers.Count());
	}
}
