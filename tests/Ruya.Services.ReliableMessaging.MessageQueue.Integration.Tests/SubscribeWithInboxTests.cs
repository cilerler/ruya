using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

	public sealed class ScopedProbe;

	public sealed record TestPayload(string Value);

	private const string ProviderKey = "memory";

	private ServiceProvider _services = null!;
	private IMessageQueue _queue = null!;
	private TestAtomicInboxStore _inboxStore = null!;
	private ConcurrentQueue<EventId> _logEvents = null!;

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
		_logEvents = new ConcurrentQueue<EventId>();
		services.AddLogging(builder => builder.AddProvider(new CapturingLoggerProvider(_logEvents)));
		services.AddOptions();
		services.AddScoped<ScopedProbe>();

		services
			.AddMessageQueue()
			.AddInMemoryProvider(options =>
			{
				options.MaxRetryAttempts = 2;
				options.RetryDelay = TimeSpan.FromMilliseconds(25);
			});

		_inboxStore = new TestAtomicInboxStore();
		services.AddScoped<IAtomicInboxStore<TestContext>>(_ => _inboxStore);

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
	public async Task SubscribeWithInboxAndPostCommit_FirstReceipt_ObservesAfterAtomicCommit()
	{
		const string topic = "test.inbox.first";
		const string consumerName = "StoreSnapshotProjector";
		var delivered = new TaskCompletionSource<TestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
		var workCompleted = new TaskCompletionSource<InboxWorkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var commitObserved = new TaskCompletionSource<TestPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
		var storeReturned = false;

		_inboxStore.Execute = async (work, cancellationToken) =>
		{
			var workResult = await work(cancellationToken);
			workCompleted.TrySetResult(workResult);
			var result = workResult == InboxWorkResult.Processed
				? InboxExecutionResult.Processed
				: InboxExecutionResult.Abandoned;
			storeReturned = true;
			return result;
		};

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		await using var subscription = await _queue.SubscribeWithInboxAndPostCommitAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			(services, context) =>
			{
				Assert.IsNotNull(services.GetRequiredService<ScopedProbe>());
				Assert.AreSame(_inboxStore, services.GetRequiredService<IAtomicInboxStore<TestContext>>());
				delivered.TrySetResult(context.Envelope.Payload);
				return Task.FromResult(MessageResult.Success());
			},
			(services, context) =>
			{
				Assert.IsTrue(storeReturned, "The observer must run only after ExecuteOnceAsync reports the commit.");
				Assert.AreSame(_inboxStore, services.GetRequiredService<IAtomicInboxStore<TestContext>>());
				commitObserved.TrySetResult(context.Envelope.Payload);
				return Task.CompletedTask;
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("hello"));

		var payload = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var outcome = await workCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var observedPayload = await commitObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.AreEqual("hello", payload.Value);
		Assert.AreEqual("hello", observedPayload.Value);
		Assert.AreEqual(InboxWorkResult.Processed, outcome);
		Assert.AreEqual(consumerName, _inboxStore.ConsumerName);
		Assert.AreEqual(topic, _inboxStore.Topic);
	}

	[TestMethod]
	public async Task SubscribeWithInboxAndPostCommit_DuplicateReceipt_SkipsHandlerAndObserver()
	{
		const string topic = "test.inbox.duplicate";
		const string consumerName = "StoreSnapshotProjector";
		var executionObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var handlerInvoked = false;
		var observerInvoked = false;

		_inboxStore.Execute = (_, _) =>
		{
			executionObserved.TrySetResult();
			return Task.FromResult(InboxExecutionResult.Duplicate);
		};

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		await using var subscription = await _queue.SubscribeWithInboxAndPostCommitAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			(_, _) =>
			{
				handlerInvoked = true;
				return Task.FromResult(MessageResult.Success());
			},
			(_, _) =>
			{
				observerInvoked = true;
				return Task.CompletedTask;
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("should-be-skipped"));
		await executionObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await subscription.DisposeAsync();

		Assert.IsFalse(handlerInvoked, "Handler must not be invoked for a committed duplicate.");
		Assert.IsFalse(observerInvoked, "Observer must not be invoked for a committed duplicate.");
	}

	[TestMethod]
	public async Task SubscribeWithInboxAndPostCommit_HandlerReturnsRetry_AbandonsAtomicWorkAndSkipsObserver()
	{
		const string topic = "test.inbox.retry";
		const string consumerName = "StoreSnapshotProjector";
		var workCompleted = new TaskCompletionSource<InboxWorkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var observerInvoked = false;

		_inboxStore.Execute = async (work, cancellationToken) =>
		{
			var workResult = await work(cancellationToken);
			workCompleted.TrySetResult(workResult);
			return InboxExecutionResult.Abandoned;
		};

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		await using var subscription = await _queue.SubscribeWithInboxAndPostCommitAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			(_, _) => Task.FromResult(MessageResult.Retry("simulated")),
			(_, _) =>
			{
				observerInvoked = true;
				return Task.CompletedTask;
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("retry-me"));

		var outcome = await workCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await subscription.DisposeAsync();
		Assert.AreEqual(InboxWorkResult.Abandoned, outcome);
		Assert.IsFalse(observerInvoked, "Observer must not be invoked for Retry.");
	}

	[TestMethod]
	public async Task SubscribeWithInboxAndPostCommit_HandlerReturnsReject_AbandonsAtomicWorkAndSkipsObserver()
	{
		const string topic = "test.inbox.reject";
		const string consumerName = "StoreSnapshotProjector";
		var workCompleted = new TaskCompletionSource<InboxWorkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var observerInvoked = false;

		_inboxStore.Execute = async (work, cancellationToken) =>
		{
			var workResult = await work(cancellationToken);
			workCompleted.TrySetResult(workResult);
			return InboxExecutionResult.Abandoned;
		};

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		await using var subscription = await _queue.SubscribeWithInboxAndPostCommitAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			(_, _) => Task.FromResult(MessageResult.Reject("simulated")),
			(_, _) =>
			{
				observerInvoked = true;
				return Task.CompletedTask;
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("reject-me"));

		var outcome = await workCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await subscription.DisposeAsync();
		Assert.AreEqual(InboxWorkResult.Abandoned, outcome);
		Assert.IsFalse(observerInvoked, "Observer must not be invoked for Reject.");
	}

	[TestMethod]
	public async Task SubscribeWithInboxAndPostCommit_HandlerThrows_SkipsObserver()
	{
		const string topic = "test.inbox.exception";
		const string consumerName = "StoreSnapshotProjector";
		var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var observerInvoked = false;

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		await using var subscription = await _queue.SubscribeWithInboxAndPostCommitAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			(_, _) =>
			{
				handlerEntered.TrySetResult();
				throw new InvalidOperationException("simulated");
			},
			(_, _) =>
			{
				observerInvoked = true;
				return Task.CompletedTask;
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("throw"));
		await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await subscription.DisposeAsync();

		Assert.IsFalse(observerInvoked, "Observer must not be invoked when the handler throws.");
	}

	[TestMethod]
	public async Task SubscribeWithInboxAndPostCommit_ObserverThrows_LogsAndPreservesSuccess()
	{
		const string topic = "test.inbox.observer-failure";
		const string consumerName = "StoreSnapshotProjector";
		var observerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var executionCount = 0;

		_inboxStore.Execute = async (work, cancellationToken) =>
		{
			Interlocked.Increment(ref executionCount);
			var workResult = await work(cancellationToken);
			return workResult == InboxWorkResult.Processed
				? InboxExecutionResult.Processed
				: InboxExecutionResult.Abandoned;
		};

		var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
		await using var subscription = await _queue.SubscribeWithInboxAndPostCommitAsync<TestPayload, TestContext>(
			topic,
			consumerName,
			scopeFactory,
			(_, _) => Task.FromResult(MessageResult.Success()),
			(_, _) =>
			{
				observerEntered.TrySetResult();
				throw new InvalidOperationException("simulated observer failure");
			},
			new SubscribeOptions { AutoAck = true });

		await _queue.PublishAsync(topic, new TestPayload("commit"));
		await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Task.Delay(TimeSpan.FromMilliseconds(200));
		await subscription.DisposeAsync();

		Assert.AreEqual(1, executionCount, "An observer failure must not make the broker retry committed work.");
		Assert.IsTrue(
			_logEvents.Contains(new EventId(8101, "InboxPostCommitObserverFailed")),
			"The observer failure must be logged with the documented event ID.");
	}

	private sealed class TestAtomicInboxStore : IAtomicInboxStore<TestContext>
	{
		public Func<Func<CancellationToken, Task<InboxWorkResult>>, CancellationToken, Task<InboxExecutionResult>> Execute { get; set; }
			= static async (work, cancellationToken) =>
			{
				var workResult = await work(cancellationToken);
				return workResult == InboxWorkResult.Processed
					? InboxExecutionResult.Processed
					: InboxExecutionResult.Abandoned;
			};

		public string? ConsumerName { get; private set; }

		public string? Topic { get; private set; }

		public Task<InboxExecutionResult> ExecuteOnceAsync(
			string consumerName,
			string messageId,
			string topic,
			Func<CancellationToken, Task<InboxWorkResult>> work,
			CancellationToken cancellationToken)
		{
			ConsumerName = consumerName;
			Topic = topic;
			return Execute(work, cancellationToken);
		}
	}

	private sealed class CapturingLoggerProvider(ConcurrentQueue<EventId> events) : ILoggerProvider
	{
		public ILogger CreateLogger(string categoryName) => new CapturingLogger(events);

		public void Dispose()
		{
		}

		private sealed class CapturingLogger(ConcurrentQueue<EventId> events) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state)
				where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => true;

			public void Log<TState>(
				LogLevel logLevel,
				EventId eventId,
				TState state,
				Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				events.Enqueue(eventId);
			}
		}
	}
}
