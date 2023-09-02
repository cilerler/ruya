using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Bus.Tests;

[TestClass]
public class InMemoryEventBusSubscriptionsManagerTest
{
	private static TestContext _testContext;

	[ClassInitialize]
	public static void ClassInitialize(TestContext testContext)
	{
		_testContext = testContext;
	}

	[TestMethod]
	public void After_Creation_Should_Be_Empty()
	{
		var manager = new InMemoryEventBusSubscriptionsManager();
		Assert.IsTrue(manager.IsEmpty);
	}

	[TestMethod]
	public void After_One_Event_Subscription_Should_Contain_The_Event()
	{
		var manager = new InMemoryEventBusSubscriptionsManager();
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		Assert.IsTrue(manager.HasSubscriptionsForEvent<TestIntegrationEvent>());
	}

	[TestMethod]
	public void After_All_Subscriptions_Are_Deleted_Event_Should_No_Longer_Exists()
	{
		var manager = new InMemoryEventBusSubscriptionsManager();
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		manager.RemoveSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		Assert.IsFalse(manager.HasSubscriptionsForEvent<TestIntegrationEvent>());
	}

	[TestMethod]
	public void Deleting_Last_Subscription_Should_Raise_On_Deleted_Event()
	{
		var raised = false;
		var manager = new InMemoryEventBusSubscriptionsManager();
		manager.OnEventRemoved += (o, e) => raised = true;
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		manager.RemoveSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		Assert.IsTrue(raised);
	}

	[TestMethod]
	public void Get_Handlers_For_Event_Should_Return_All_Handlers()
	{
		var manager = new InMemoryEventBusSubscriptionsManager();
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationEventHandler>();
		manager.AddSubscription<TestIntegrationEvent, TestIntegrationOtherEventHandler>();
		IEnumerable<InMemoryEventBusSubscriptionsManager.SubscriptionInfo> handlers = manager.GetHandlersForEvent<TestIntegrationEvent>();
		Assert.AreEqual(2, handlers.Count());
	}
}
