using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Utilities;

namespace Ruya.Services.MessageQueue.Integration.Tests;

[TestClass]
public class RoutingPatternMatcherTests
{
    [TestMethod]
    [DataRow("orders", "orders.#", true)]
    [DataRow("orders.created", "orders.#", true)]
    [DataRow("orders.us.electronics.created", "orders.#", true)]
    [DataRow("inventory.updated", "orders.#", false)]
    public void Matches_ShouldWorkCorrectly(string routingKey, string pattern, bool expected)
    {
        var result = RoutingPatternMatcher.Matches(routingKey, pattern);
        Assert.AreEqual(expected, result, $"Failed for Key: {routingKey}, Pattern: {pattern}");
    }
}
