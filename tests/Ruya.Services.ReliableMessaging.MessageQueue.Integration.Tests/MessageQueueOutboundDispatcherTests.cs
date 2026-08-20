using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.InMemory;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.ReliableMessaging;
using Ruya.Services.ReliableMessaging.Extensions;
using Ruya.Services.ReliableMessaging.MessageQueue;
using Ruya.Services.ReliableMessaging.MessageQueue.Extensions;

namespace Ruya.Services.ReliableMessaging.MessageQueue.Integration.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class MessageQueueOutboundDispatcherTests
{
	public sealed record TestPayload(string Name, int Quantity);

	private const string ProviderKey = "memory";
	private const string AlternateProviderKey = "alternate";
	private const string TopicKey = "test.dispatcher.topic";
	private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

	private ServiceProvider _services = null!;
	private IMessageQueue _queue = null!;
	private IMessageQueue _alternateQueue = null!;

	[TestInitialize]
	public async Task InitAsync()
	{
		var services = new ServiceCollection();

		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[$"MessageQueue:Providers:{ProviderKey}:Type"] = "InMemory",
				[$"MessageQueue:Providers:{ProviderKey}:Enabled"] = "true",
				[$"MessageQueue:Providers:{AlternateProviderKey}:Type"] = "InMemory",
				[$"MessageQueue:Providers:{AlternateProviderKey}:Enabled"] = "true",
				["MessageQueue:DefaultProvider"] = ProviderKey,
			})
			.Build();

		services.AddSingleton<IConfiguration>(config);
		services.AddLogging();
		services.AddOptions();

		services
			.AddMessageQueue()
			.AddInMemoryProvider();

		services.AddOptions<MessageQueueDispatcherOptions>()
			.Configure(options => options.QueueName = ProviderKey);

		services.AddSingleton<MessageQueueOutboundDispatcher>();

		_services = services.BuildServiceProvider();

		var factory = _services.GetRequiredService<IMessageQueueFactory>();
		_queue = await factory.CreateQueueAsync(ProviderKey, CancellationToken.None);
		_alternateQueue = await factory.CreateQueueAsync(AlternateProviderKey, CancellationToken.None);
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
			PayloadJson = JsonSerializer.Serialize(payload, _serializerOptions),
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
	public async Task DispatchAsync_WithoutDispatcherName_FallsBackToOptionsQueueName()
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
			PayloadJson = JsonSerializer.Serialize(payload, _serializerOptions),
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
	public async Task DispatchAsync_WithDispatcherName_UsesSpecifiedProvider()
	{
		var received = new TaskCompletionSource<TestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
		var topic = TopicKey + ".provider";

		await _alternateQueue.SubscribeAsync<TestPayload>(
			topic,
			context =>
			{
				received.TrySetResult(context.Envelope.Payload);
				return Task.FromResult(MessageResult.Success());
			},
			new SubscribeOptions { AutoAck = true });

		var dispatcher = _services.GetRequiredService<MessageQueueOutboundDispatcher>();
		var payload = new TestPayload("alternate", 7);
		var envelope = new ReliableMessageEnvelope
		{
			Topic = topic,
			DispatcherName = AlternateProviderKey,
			PayloadJson = JsonSerializer.Serialize(payload, _serializerOptions),
			PayloadType = typeof(TestPayload).AssemblyQualifiedName!,
		};

		await dispatcher.DispatchAsync(envelope, CancellationToken.None);

		var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual("alternate", actual.Name);
	}

	[TestMethod]
	public async Task DispatchAsync_PreservesOutboxMessageIdOnBrokerEnvelope()
	{
		var received = new TaskCompletionSource<MessageEnvelope<TestPayload>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var topic = TopicKey + ".message-id";

		await _queue.SubscribeAsync<TestPayload>(
			topic,
			context =>
			{
				received.TrySetResult(context.Envelope);
				return Task.FromResult(MessageResult.Success());
			},
			new SubscribeOptions { AutoAck = true });

		var dispatcher = _services.GetRequiredService<MessageQueueOutboundDispatcher>();
		var envelope = new ReliableMessageEnvelope
		{
			MessageId = Guid.Parse("7b7c7597-6cb2-42d6-a8d7-d8bdcf17f6f4"),
			Topic = topic,
			DispatcherName = ProviderKey,
			PayloadJson = JsonSerializer.Serialize(new TestPayload("stable", 1), _serializerOptions),
			PayloadType = typeof(TestPayload).AssemblyQualifiedName!,
		};

		await dispatcher.DispatchAsync(envelope, CancellationToken.None);

		var delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.AreEqual(envelope.MessageId.ToString("D"), delivered.MessageId);
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
			PayloadJson = JsonSerializer.Serialize(payload, _serializerOptions),
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

	[TestMethod]
	public async Task DispatchAsync_WithRegisteredContext_UsesSourceGeneratedPayloadMetadata()
	{
		using var resolverServices = CreateResolverServices(
			DispatcherJsonSerializerContext.Default);
		var resolver = resolverServices.GetRequiredService<IMessageJsonTypeInfoResolver>();
		var (dispatcher, queue) = CreateIsolatedDispatcher(resolver);
		SourceGeneratedDispatcherPayload? publishedPayload = null;
		queue.Setup(instance => instance.PublishAsync(
				It.IsAny<string>(),
				It.IsAny<SourceGeneratedDispatcherPayload>(),
				It.IsAny<PublishOptions?>(),
				It.IsAny<CancellationToken>()))
			.Callback<string, SourceGeneratedDispatcherPayload, PublishOptions?, CancellationToken>(
				(_, payload, _, _) => publishedPayload = payload)
			.ReturnsAsync("published");

		var payload = new SourceGeneratedDispatcherPayload("source-generated");
		var payloadJson = JsonSerializer.Serialize(
			payload,
			DispatcherJsonSerializerContext.Default.SourceGeneratedDispatcherPayload);
		StringAssert.Contains(
			payloadJson,
			"\"stable_value\":\"source-generated\"",
			StringComparison.Ordinal);

		await dispatcher.DispatchAsync(CreateEnvelope(payloadJson, typeof(SourceGeneratedDispatcherPayload)));

		Assert.IsNotNull(publishedPayload);
		Assert.AreEqual("source-generated", publishedPayload.StableValue);
	}

	[TestMethod]
	public async Task DispatchAsync_WithRegisteredContextsAndMissingPayloadMetadata_Throws()
	{
		using var resolverServices = CreateResolverServices(
			DispatcherJsonSerializerContext.Default);
		var resolver = resolverServices.GetRequiredService<IMessageJsonTypeInfoResolver>();
		var (dispatcher, queue) = CreateIsolatedDispatcher(resolver);
		var payload = new UnregisteredDispatcherPayload("reflection-only");
		var envelope = CreateEnvelope(
			JsonSerializer.Serialize(payload, _serializerOptions),
			typeof(UnregisteredDispatcherPayload));

		var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(
			() => dispatcher.DispatchAsync(envelope));

		StringAssert.Contains(
			exception.Message,
			typeof(UnregisteredDispatcherPayload).FullName!,
			StringComparison.Ordinal);
		queue.Verify(instance => instance.PublishAsync(
				It.IsAny<string>(),
				It.IsAny<UnregisteredDispatcherPayload>(),
				It.IsAny<PublishOptions?>(),
				It.IsAny<CancellationToken>()),
			Times.Never);
	}

	[TestMethod]
	public async Task DispatchAsync_WithoutRegisteredContexts_RetainsLegacyReflectionDeserialization()
	{
		using var resolverServices = CreateResolverServices();
		var resolver = resolverServices.GetRequiredService<IMessageJsonTypeInfoResolver>();
		var (dispatcher, queue) = CreateIsolatedDispatcher(resolver);
		UnregisteredDispatcherPayload? publishedPayload = null;
		queue.Setup(instance => instance.PublishAsync(
				It.IsAny<string>(),
				It.IsAny<UnregisteredDispatcherPayload>(),
				It.IsAny<PublishOptions?>(),
				It.IsAny<CancellationToken>()))
			.Callback<string, UnregisteredDispatcherPayload, PublishOptions?, CancellationToken>(
				(_, payload, _, _) => publishedPayload = payload)
			.ReturnsAsync("published");
		var payload = new UnregisteredDispatcherPayload("legacy");

		await dispatcher.DispatchAsync(CreateEnvelope(
			JsonSerializer.Serialize(payload, _serializerOptions),
			typeof(UnregisteredDispatcherPayload)));

		Assert.IsNotNull(publishedPayload);
		Assert.AreEqual("legacy", publishedPayload.Value);
	}

	[TestMethod]
	public async Task DispatchAsync_WithMultipleRegisteredContexts_ResolvesEachPayloadInOrder()
	{
		using var resolverServices = CreateResolverServices(
			DispatcherJsonSerializerContext.Default,
			SecondDispatcherJsonSerializerContext.Default);
		var resolver = resolverServices.GetRequiredService<IMessageJsonTypeInfoResolver>();
		var (dispatcher, queue) = CreateIsolatedDispatcher(resolver);
		SourceGeneratedDispatcherPayload? firstPublished = null;
		SecondSourceGeneratedDispatcherPayload? secondPublished = null;
		queue.Setup(instance => instance.PublishAsync(
				It.IsAny<string>(),
				It.IsAny<SourceGeneratedDispatcherPayload>(),
				It.IsAny<PublishOptions?>(),
				It.IsAny<CancellationToken>()))
			.Callback<string, SourceGeneratedDispatcherPayload, PublishOptions?, CancellationToken>(
				(_, payload, _, _) => firstPublished = payload)
			.ReturnsAsync("first");
		queue.Setup(instance => instance.PublishAsync(
				It.IsAny<string>(),
				It.IsAny<SecondSourceGeneratedDispatcherPayload>(),
				It.IsAny<PublishOptions?>(),
				It.IsAny<CancellationToken>()))
			.Callback<string, SecondSourceGeneratedDispatcherPayload, PublishOptions?, CancellationToken>(
				(_, payload, _, _) => secondPublished = payload)
			.ReturnsAsync("second");

		await dispatcher.DispatchAsync(CreateEnvelope(
			JsonSerializer.Serialize(
				new SourceGeneratedDispatcherPayload("first-context"),
				DispatcherJsonSerializerContext.Default.SourceGeneratedDispatcherPayload),
			typeof(SourceGeneratedDispatcherPayload)));
		await dispatcher.DispatchAsync(CreateEnvelope(
			JsonSerializer.Serialize(
				new SecondSourceGeneratedDispatcherPayload("second-context"),
				SecondDispatcherJsonSerializerContext.Default.SecondSourceGeneratedDispatcherPayload),
			typeof(SecondSourceGeneratedDispatcherPayload)));

		Assert.AreEqual("first-context", firstPublished?.StableValue);
		Assert.AreEqual("second-context", secondPublished?.Value);
	}

	[TestMethod]
	public void AddMessageQueueOutboundDispatcher_BlankFallbackProvider_FailsStartupValidation()
	{
		using var provider = CreateDispatcherValidationProvider("   ");
		var startupValidator = provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>();

		var exception = Assert.ThrowsExactly<Microsoft.Extensions.Options.OptionsValidationException>(
			startupValidator.Validate);
		StringAssert.Contains(exception.Message, "QueueName", StringComparison.Ordinal);
	}

	[TestMethod]
	public void AddMessageQueueOutboundDispatcher_MissingFallbackProvider_FailsStartupValidation()
	{
		using var provider = CreateDispatcherValidationProvider(null);
		var startupValidator = provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>();

		var exception = Assert.ThrowsExactly<Microsoft.Extensions.Options.OptionsValidationException>(
			startupValidator.Validate);
		StringAssert.Contains(exception.Message, "QueueName", StringComparison.Ordinal);
	}

	[TestMethod]
	public void AddMessageQueueOutboundDispatcher_UnconfiguredFallbackProvider_FailsStartupValidation()
	{
		using var provider = CreateDispatcherValidationProvider("missing");
		var startupValidator = provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>();

		var exception = Assert.ThrowsExactly<Microsoft.Extensions.Options.OptionsValidationException>(
			startupValidator.Validate);
		StringAssert.Contains(exception.Message, "not configured", StringComparison.Ordinal);
	}

	[TestMethod]
	public void AddMessageQueueOutboundDispatcher_DisabledFallbackProvider_FailsStartupValidation()
	{
		using var provider = CreateDispatcherValidationProvider(
			ProviderKey,
			new Dictionary<string, string?>
			{
				[$"MessageQueue:Providers:{ProviderKey}:Type"] = "InMemory",
				[$"MessageQueue:Providers:{ProviderKey}:Enabled"] = "false",
			});
		var startupValidator = provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>();

		var exception = Assert.ThrowsExactly<Microsoft.Extensions.Options.OptionsValidationException>(
			startupValidator.Validate);
		StringAssert.Contains(exception.Message, "disabled", StringComparison.Ordinal);
	}

	[TestMethod]
	public void AddMessageQueueOutboundDispatcher_EnabledFallbackProvider_SucceedsStartupValidation()
	{
		using var provider = CreateDispatcherValidationProvider(
			ProviderKey,
			new Dictionary<string, string?>
			{
				[$"MessageQueue:Providers:{ProviderKey}:Type"] = "InMemory",
				[$"MessageQueue:Providers:{ProviderKey}:Enabled"] = "true",
			});
		var startupValidator = provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>();

		startupValidator.Validate();
	}

	private static ServiceProvider CreateResolverServices(params JsonSerializerContext[] contexts)
	{
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
		var builder = services.AddMessageQueue();
		foreach (var context in contexts)
		{
			builder.AddJsonSerializerContext(context);
		}

		return services.BuildServiceProvider();
	}

	private static ServiceProvider CreateDispatcherValidationProvider(
		string? queueName,
		Dictionary<string, string?>? configurationValues = null)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(configurationValues)
			.Build();
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddMessageQueue();
		var reliableMessaging = services.AddReliableMessaging();
		if (queueName is null)
		{
			reliableMessaging.AddMessageQueueOutboundDispatcher();
		}
		else
		{
			reliableMessaging.AddMessageQueueOutboundDispatcher(options => options.QueueName = queueName);
		}

		return services.BuildServiceProvider();
	}

	private static (MessageQueueOutboundDispatcher Dispatcher, Mock<IMessageQueue> Queue)
		CreateIsolatedDispatcher(IMessageJsonTypeInfoResolver resolver)
	{
		var queue = new Mock<IMessageQueue>();
		var factory = new Mock<IMessageQueueFactory>();
		factory.Setup(instance => instance.CreateQueueAsync(
				It.IsAny<string>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(queue.Object);
		var options = Microsoft.Extensions.Options.Options.Create(
			new MessageQueueDispatcherOptions { QueueName = ProviderKey });

		return (new MessageQueueOutboundDispatcher(factory.Object, options, resolver), queue);
	}

	private static ReliableMessageEnvelope CreateEnvelope(string payloadJson, Type payloadType) => new()
	{
		Topic = TopicKey,
		DispatcherName = ProviderKey,
		PayloadJson = payloadJson,
		PayloadType = payloadType.AssemblyQualifiedName!,
	};
}

public sealed record SourceGeneratedDispatcherPayload(string StableValue);

public sealed record SecondSourceGeneratedDispatcherPayload(string Value);

public sealed record UnregisteredDispatcherPayload(string Value);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SourceGeneratedDispatcherPayload))]
public sealed partial class DispatcherJsonSerializerContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(SecondSourceGeneratedDispatcherPayload))]
public sealed partial class SecondDispatcherJsonSerializerContext : JsonSerializerContext
{
}
