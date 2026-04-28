using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.Unit.Tests.Outbox;

[TestClass]
[TestCategory("Unit")]
public sealed class OutboxPublisherTests
{
	private sealed class TestContext;

	private sealed record TestPayload(string Value, int Count);

	private static OutboxPublisher<TestContext> CreatePublisher(
		OutboxBuffer<TestContext> buffer,
		OutboxOptions? outboxOptions = null)
	{
		var options = Options.Create(new ReliableMessagingOptions
		{
			Outbox = outboxOptions ?? new OutboxOptions(),
		});
		return new OutboxPublisher<TestContext>(buffer, options);
	}

	[TestMethod]
	public async Task EnqueueAsync_WithValidPayload_AddsEnvelopeToBuffer()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var publisher = CreatePublisher(buffer);
		var payload = new TestPayload("hello", 42);

		var envelope = await publisher.EnqueueAsync("test.topic", payload);

		Assert.AreEqual(1, buffer.Count);
		Assert.AreEqual("test.topic", envelope.Topic);
		Assert.AreNotEqual(Guid.Empty, envelope.MessageId);
	}

	[TestMethod]
	public async Task EnqueueAsync_SerializesPayloadAsJson()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var publisher = CreatePublisher(buffer);
		var payload = new TestPayload("hello", 42);

		var envelope = await publisher.EnqueueAsync("test.topic", payload);

		var roundTrip = JsonSerializer.Deserialize<TestPayload>(envelope.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		Assert.IsNotNull(roundTrip);
		Assert.AreEqual("hello", roundTrip!.Value);
		Assert.AreEqual(42, roundTrip.Count);
	}

	[TestMethod]
	public async Task EnqueueAsync_StampsAssemblyQualifiedPayloadType()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var publisher = CreatePublisher(buffer);

		var envelope = await publisher.EnqueueAsync("test.topic", new TestPayload("x", 1));

		Assert.IsTrue(envelope.PayloadType.StartsWith(typeof(TestPayload).FullName!, StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task EnqueueAsync_WithOverrides_AppliesDispatcherNameAndHeaders()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var publisher = CreatePublisher(buffer);
		var overrides = new OutboxPublishOverrides
		{
			DispatcherName = "custom-provider",
			Headers = new Dictionary<string, string> { ["Source"] = "recipe-service" },
		};

		var envelope = await publisher.EnqueueAsync("test.topic", new TestPayload("x", 1), overrides);

		Assert.AreEqual("custom-provider", envelope.DispatcherName);
		Assert.IsNotNull(envelope.Headers);
		Assert.AreEqual("recipe-service", envelope.Headers!["Source"]);
	}

	[TestMethod]
	public async Task EnqueueAsync_WithoutOverrides_FallsBackToDefaultDispatcherName()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var publisher = CreatePublisher(buffer, new OutboxOptions { DefaultDispatcherName = "default-provider" });

		var envelope = await publisher.EnqueueAsync("test.topic", new TestPayload("x", 1));

		Assert.AreEqual("default-provider", envelope.DispatcherName);
	}

	[TestMethod]
	public async Task EnqueueAsync_WithEmptyTopic_ThrowsArgumentException()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var publisher = CreatePublisher(buffer);

		await Assert.ThrowsExactlyAsync<ArgumentException>(
			() => publisher.EnqueueAsync(string.Empty, new TestPayload("x", 1)));
	}

	[TestMethod]
	public async Task EnqueueAsync_WithNullPayload_ThrowsArgumentNullException()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var publisher = CreatePublisher(buffer);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => publisher.EnqueueAsync<TestPayload>("test.topic", null!));
	}
}
