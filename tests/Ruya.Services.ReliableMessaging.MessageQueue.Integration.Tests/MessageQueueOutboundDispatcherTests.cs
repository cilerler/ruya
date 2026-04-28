using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.InMemory;
using Ruya.Services.ReliableMessaging;
using Ruya.Services.ReliableMessaging.MessageQueue;

namespace Ruya.Services.ReliableMessaging.MessageQueue.Integration.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class MessageQueueOutboundDispatcherTests
{
	public sealed record TestPayload(string Name, int Quantity);

	private const string ProviderKey = "memory";
	private const string TopicKey = "test.dispatcher.topic";

	private ServiceProvider _services = null!;
	private IMessageQueue _queue = null!;

	[TestInitialize]
	public async Task InitAsync()
	{
		var services = new ServiceCollection();

		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[$"MessageQueue:Providers:{ProviderKey}:Type"] = "InMemory",
				[$"MessageQueue:Providers:{ProviderKey}:Enabled"] = "true",
				["MessageQueue:DefaultProvider"] = ProviderKey,
			})
			.Build();

		services.AddSingleton<IConfiguration>(config);
		services.AddLogging();
		services.AddOptions();

		services
			.AddMessageQueue(config)
			.AddInMemoryProvider();

		services.AddOptions<MessageQueueDispatcherOptions>()
			.Configure(options => options.QueueName = ProviderKey);

		services.AddSingleton<MessageQueueOutboundDispatcher>();

		_services = services.BuildServiceProvider();

		var factory = _services.GetRequiredService<IMessageQueueFactory>();
		_queue = await factory.CreateQueueAsync(ProviderKey, CancellationToken.None);
	}

	[TestCleanup]
	public async Task CleanupAsync()
	{
		await _services.DisposeAsync();
	}

	[TestMethod]
	public async Task DispatchAsync_WithTypedPayload_PublishesToSubscriberWithOriginalType()
	{
		var received = new TaskCompletionSource<TestPayload>();

		await _queue.SubscribeAsync<TestPayload>(
			TopicKey,
			context =>
			{
				received.TrySetResult(context.Envelope.Payload);
				return Task.FromResult(MessageResult.Success());
			},
			new SubscribeOptions { AutoAck = true });

		var dispatcher = _services.GetRequiredService<MessageQueueOutboundDispatcher>();
		var payload = new TestPayload("sourdough", 3);
		var envelope = new ReliableMessageEnvelope
		{
			Topic = TopicKey,
			DispatcherName = ProviderKey,
			PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
			PayloadType = typeof(TestPayload).AssemblyQualifiedName!,
		};

		await dispatcher.DispatchAsync(envelope, CancellationToken.None);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using (cts.Token.Register(() => received.TrySetCanceled()))
		{
			var actual = await received.Task;
			Assert.AreEqual(payload.Name, actual.Name);
			Assert.AreEqual(payload.Quantity, actual.Quantity);
		}
	}

	[TestMethod]
	public async Task DispatchAsync_WithUnknownDispatcherName_FallsBackToOptionsQueueName()
	{
		var received = new TaskCompletionSource<TestPayload>();

		await _queue.SubscribeAsync<TestPayload>(
			TopicKey + ".fallback",
			context =>
			{
				received.TrySetResult(context.Envelope.Payload);
				return Task.FromResult(MessageResult.Success());
			},
			new SubscribeOptions { AutoAck = true });

		var dispatcher = _services.GetRequiredService<MessageQueueOutboundDispatcher>();
		var payload = new TestPayload("whole-wheat", 1);
		var envelope = new ReliableMessageEnvelope
		{
			Topic = TopicKey + ".fallback",
			DispatcherName = null, // should fall back to options.QueueName
			PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
			PayloadType = typeof(TestPayload).AssemblyQualifiedName!,
		};

		await dispatcher.DispatchAsync(envelope, CancellationToken.None);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using (cts.Token.Register(() => received.TrySetCanceled()))
		{
			var actual = await received.Task;
			Assert.AreEqual("whole-wheat", actual.Name);
		}
	}

	[TestMethod]
	public async Task DispatchAsync_WithHeaderOverrides_StampsCorrelationAndCausationOnEnvelope()
	{
		var received = new TaskCompletionSource<MessageEnvelope<TestPayload>>();

		await _queue.SubscribeAsync<TestPayload>(
			TopicKey + ".headers",
			context =>
			{
				received.TrySetResult(context.Envelope);
				return Task.FromResult(MessageResult.Success());
			},
			new SubscribeOptions { AutoAck = true });

		var dispatcher = _services.GetRequiredService<MessageQueueOutboundDispatcher>();
		var payload = new TestPayload("rye", 2);
		var envelope = new ReliableMessageEnvelope
		{
			Topic = TopicKey + ".headers",
			DispatcherName = ProviderKey,
			PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
			PayloadType = typeof(TestPayload).AssemblyQualifiedName!,
			Headers = new Dictionary<string, string>
			{
				["CorrelationId"] = "corr-abc",
				["CausationId"] = "cause-xyz",
				["Source"] = "integration-test",
				["CustomKey"] = "custom-value",
			},
		};

		await dispatcher.DispatchAsync(envelope, CancellationToken.None);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using (cts.Token.Register(() => received.TrySetCanceled()))
		{
			var delivered = await received.Task;
			Assert.AreEqual("corr-abc", delivered.CorrelationId);
			Assert.AreEqual("cause-xyz", delivered.CausationId);
			Assert.AreEqual("integration-test", delivered.Source);
			Assert.IsNotNull(delivered.Headers);
			Assert.AreEqual("custom-value", delivered.Headers!["CustomKey"]);
		}
	}
}
