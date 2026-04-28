using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.InMemory;
using Ruya.Services.ReliableMessaging.Inbox;
using Ruya.Services.ReliableMessaging.MessageQueue;

namespace Ruya.Services.ReliableMessaging.MessageQueue.Integration.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class SubscribeWithInboxTests
{
	public sealed class TestContext;

	public sealed record TestPayload(string Value);

	private const string ProviderKey = "memory";

	private ServiceProvider _services = null!;
	private IMessageQueue _queue = null!;
	private Mock<IInboxStore<TestContext>> _inboxStoreMock = null!;

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

		_inboxStoreMock = new Mock<IInboxStore<TestContext>>();
		services.AddScoped(_ => _inboxStoreMock.Object);

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
	public async Task SubscribeWithInbox_FirstReceipt_InvokesHandlerAndMarksProcessed()
	{
		const string topic = "test.inbox.first";
		const string consumerName = "StoreSnapshotProjector";

		_inboxStoreMock
			.Setup(s => s.TryRecordAsync(consumerName, It.IsAny<string>(), topic, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		var handlerInvoked = new TaskCompletionSource<TestPayload>();
		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

		await _queue.SubscribeWithInboxAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			context =>
			{
				handlerInvoked.TrySetResult(context.Envelope.Payload);
				return Task.FromResult(MessageResult.Success());
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("hello"));

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using (cts.Token.Register(() => handlerInvoked.TrySetCanceled()))
		{
			var delivered = await handlerInvoked.Task;
			Assert.AreEqual("hello", delivered.Value);
		}

		// Give the wrapper a moment to call MarkProcessedAsync after the handler returned.
		for (var i = 0; i < 20 && _inboxStoreMock.Invocations.Count < 2; i++)
		{
			await Task.Delay(50);
		}

		_inboxStoreMock.Verify(
			s => s.MarkProcessedAsync(consumerName, It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[TestMethod]
	public async Task SubscribeWithInbox_DuplicateReceipt_SkipsHandler()
	{
		const string topic = "test.inbox.duplicate";
		const string consumerName = "StoreSnapshotProjector";

		_inboxStoreMock
			.Setup(s => s.TryRecordAsync(consumerName, It.IsAny<string>(), topic, It.IsAny<CancellationToken>()))
			.ReturnsAsync(false); // duplicate

		var handlerInvoked = false;
		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

		await _queue.SubscribeWithInboxAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			context =>
			{
				handlerInvoked = true;
				return Task.FromResult(MessageResult.Success());
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("should-be-skipped"));

		// Wait briefly to ensure the message had time to flow through.
		await Task.Delay(500);

		Assert.IsFalse(handlerInvoked, "Handler must not be invoked for duplicate receipts.");
		_inboxStoreMock.Verify(
			s => s.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	[TestMethod]
	public async Task SubscribeWithInbox_HandlerReturnsRetry_DoesNotMarkProcessed()
	{
		const string topic = "test.inbox.retry";
		const string consumerName = "StoreSnapshotProjector";

		_inboxStoreMock
			.Setup(s => s.TryRecordAsync(consumerName, It.IsAny<string>(), topic, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		var handlerInvoked = new TaskCompletionSource<bool>();
		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

		await _queue.SubscribeWithInboxAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			context =>
			{
				handlerInvoked.TrySetResult(true);
				return Task.FromResult(MessageResult.Retry("simulated"));
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("retry-me"));

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		using (cts.Token.Register(() => handlerInvoked.TrySetCanceled()))
		{
			await handlerInvoked.Task;
		}

		// Small delay for any async post-handler bookkeeping.
		await Task.Delay(200);

		_inboxStoreMock.Verify(
			s => s.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}
}
