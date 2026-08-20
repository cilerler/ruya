using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
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

	private sealed class LegacyOutboxPublisher : IOutboxPublisher<TestContext>
	{
		public Task<ReliableMessageEnvelope> EnqueueAsync<TPayload>(
			string topic,
			TPayload payload,
			OutboxPublishOverrides? options = null,
			System.Threading.CancellationToken cancellationToken = default)
			where TPayload : notnull
		{
			throw new InvalidOperationException("The legacy enqueue method should not be called by the default interface member.");
		}
	}

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
	public async Task EnqueueSourceGeneratedAsync_UsesProducerOwnedJsonTypeInfo()
	{
		var buffer = new OutboxBuffer<TestContext>();
		IOutboxPublisher<TestContext> publisher = CreatePublisher(buffer);
		var payload = new SourceGeneratedOutboxPayload("generated");

		var envelope = await publisher.EnqueueSourceGeneratedAsync(
			"test.topic",
			payload,
			OutboxPublisherJsonSerializerContext.Default.SourceGeneratedOutboxPayload);

		StringAssert.Contains(
			envelope.PayloadJson,
			"\"stable_value\":\"generated\"",
			StringComparison.Ordinal);
		Assert.IsFalse(envelope.PayloadJson.Contains("\"stableValue\"", StringComparison.Ordinal));
		Assert.AreEqual(1, buffer.Count);
	}

	[TestMethod]
	public async Task EnqueueSourceGeneratedAsync_WithNullJsonTypeInfo_ThrowsArgumentNullException()
	{
		var buffer = new OutboxBuffer<TestContext>();
		var publisher = CreatePublisher(buffer);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => publisher.EnqueueSourceGeneratedAsync<SourceGeneratedOutboxPayload>(
				"test.topic",
				new SourceGeneratedOutboxPayload("generated"),
				null!));

		Assert.AreEqual(0, buffer.Count);
	}

	[TestMethod]
	public async Task EnqueueSourceGeneratedAsync_WithLegacyPublisher_ThrowsNotSupportedException()
	{
		IOutboxPublisher<TestContext> publisher = new LegacyOutboxPublisher();

		var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(
			() => publisher.EnqueueSourceGeneratedAsync(
				"test.topic",
				new SourceGeneratedOutboxPayload("generated"),
				OutboxPublisherJsonSerializerContext.Default.SourceGeneratedOutboxPayload));

		StringAssert.Contains(
			exception.Message,
			"does not support source-generated payload metadata",
			StringComparison.Ordinal);
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

public sealed record SourceGeneratedOutboxPayload(string StableValue);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SourceGeneratedOutboxPayload))]
public sealed partial class OutboxPublisherJsonSerializerContext : JsonSerializerContext
{
}
