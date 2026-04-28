using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.Unit.Tests.Outbox;

[TestClass]
[TestCategory("Unit")]
public sealed class OutboxBufferTests
{
	private sealed class TestContext;

	[TestMethod]
	public void Add_WhenCalled_IncrementsCount()
	{
		var buffer = new OutboxBuffer<TestContext>();

		buffer.Add(CreateEnvelope("topic.one"));
		buffer.Add(CreateEnvelope("topic.two"));

		Assert.AreEqual(2, buffer.Count);
	}

	[TestMethod]
	public void Drain_WhenNotEmpty_ReturnsItemsInInsertionOrderAndClears()
	{
		var buffer = new OutboxBuffer<TestContext>();
		buffer.Add(CreateEnvelope("topic.one"));
		buffer.Add(CreateEnvelope("topic.two"));
		buffer.Add(CreateEnvelope("topic.three"));

		var drained = buffer.Drain();

		Assert.AreEqual(3, drained.Count);
		Assert.AreEqual("topic.one", drained[0].Topic);
		Assert.AreEqual("topic.two", drained[1].Topic);
		Assert.AreEqual("topic.three", drained[2].Topic);
		Assert.AreEqual(0, buffer.Count);
	}

	[TestMethod]
	public void Drain_WhenEmpty_ReturnsEmptyList()
	{
		var buffer = new OutboxBuffer<TestContext>();

		var drained = buffer.Drain();

		Assert.AreEqual(0, drained.Count);
	}

	[TestMethod]
	public async Task AddAndDrain_FromMultipleThreads_AreConcurrencySafe()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var drainedTopics = new ConcurrentBag<string>();
		const int producerCount = 16;
		const int messagesPerProducer = 64;

		var producers = new Task[producerCount];
		for (var i = 0; i < producerCount; i++)
		{
			var producerId = i;
			producers[i] = Task.Run(() =>
			{
				for (var j = 0; j < messagesPerProducer; j++)
				{
					buffer.Add(CreateEnvelope($"topic.{producerId}.{j}"));
				}
			});
		}

		var drainer = Task.Run(() =>
		{
			while (drainedTopics.Count < producerCount * messagesPerProducer)
			{
				foreach (var envelope in buffer.Drain())
				{
					drainedTopics.Add(envelope.Topic);
				}
			}
		});

		await Task.WhenAll(producers);
		foreach (var envelope in buffer.Drain())
		{
			drainedTopics.Add(envelope.Topic);
		}

		await drainer;
		Assert.AreEqual(producerCount * messagesPerProducer, drainedTopics.Count);
	}

	private static ReliableMessageEnvelope CreateEnvelope(string topic) => new()
	{
		Topic = topic,
		PayloadJson = "{}",
		PayloadType = typeof(object).AssemblyQualifiedName!,
	};
}
