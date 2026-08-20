using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Polly;
using Polly.Registry;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.Unit.Tests.Outbox;

[TestClass]
[TestCategory("Unit")]
public sealed class OutboxProcessorTests
{
	private sealed class TestContext;

	[TestMethod]
	public async Task ProcessBatchAsync_ReconstructsPersistedHeadersOnDispatchedEnvelope()
	{
		var entry = new OutboxEntry
		{
			Id = Guid.Parse("e2ce2ab4-8ac2-46a7-9dca-8f71a107e31b"),
			Topic = "test.headers",
			DispatcherName = "secondary-provider",
			PayloadJson = "{}",
			PayloadType = typeof(object).AssemblyQualifiedName!,
			HeadersJson = JsonSerializer.Serialize(new Dictionary<string, string>
			{
				["CorrelationId"] = "corr-123",
				["CustomKey"] = "custom-value",
			}),
			EnqueuedAt = DateTime.UtcNow,
		};

		var dispatched = new TaskCompletionSource<ReliableMessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
		var store = new SingleEntryOutboxStore(entry);

		var dispatcher = new Mock<IOutboundDispatcher>();
		dispatcher.Setup(candidate => candidate.DispatchAsync(It.IsAny<ReliableMessageEnvelope>(), It.IsAny<CancellationToken>()))
			.Callback<ReliableMessageEnvelope, CancellationToken>((envelope, _) => dispatched.TrySetResult(envelope))
			.Returns(Task.CompletedTask);

		var scopedServices = new Mock<IServiceProvider>();
		scopedServices.Setup(candidate => candidate.GetService(typeof(IOutboxStore<TestContext>)))
			.Returns(store);
		var scope = new Mock<IServiceScope>();
		scope.SetupGet(candidate => candidate.ServiceProvider).Returns(scopedServices.Object);
		var scopeFactory = new Mock<IServiceScopeFactory>();
		scopeFactory.Setup(candidate => candidate.CreateScope()).Returns(scope.Object);

		var pipelineProvider = new Mock<ResiliencePipelineProvider<string>>();
		pipelineProvider.Setup(candidate => candidate.GetPipeline(OutboxResiliencePipelineKey.Dispatch))
			.Returns(ResiliencePipeline.Empty);

		using var meter = new Meter(nameof(OutboxProcessorTests));
		var meterFactory = new Mock<IMeterFactory>();
		meterFactory.Setup(candidate => candidate.Create(It.IsAny<MeterOptions>())).Returns(meter);

		var options = Options.Create(new ReliableMessagingOptions
		{
			Outbox = new OutboxOptions
			{
				PollInterval = TimeSpan.FromMilliseconds(1),
				BatchSize = 1,
			},
		});

		using var processor = new OutboxProcessor<TestContext>(
			scopeFactory.Object,
			dispatcher.Object,
			pipelineProvider.Object,
			options,
			meterFactory.Object,
			NullLogger<OutboxProcessor<TestContext>>.Instance);

		await processor.StartAsync(CancellationToken.None);
		var envelope = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await store.MarkedDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
		using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await processor.StopAsync(stopTimeout.Token);

		Assert.AreEqual(entry.Id, envelope.MessageId);
		Assert.AreEqual("secondary-provider", envelope.DispatcherName);
		Assert.IsNotNull(envelope.Headers);
		Assert.AreEqual("corr-123", envelope.Headers["CorrelationId"]);
		Assert.AreEqual("custom-value", envelope.Headers["CustomKey"]);
	}

	private sealed class SingleEntryOutboxStore : IOutboxStore<TestContext>
	{
		private readonly OutboxEntry _entry;
		private int _fetchCount;

		public SingleEntryOutboxStore(OutboxEntry entry)
		{
			_entry = entry;
		}

		public TaskCompletionSource MarkedDispatched { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<IReadOnlyList<OutboxEntry>> FetchPendingAsync(
			int batchSize,
			CancellationToken cancellationToken)
		{
			IReadOnlyList<OutboxEntry> entries = Interlocked.Increment(ref _fetchCount) == 1
				? new[] { _entry }
				: Array.Empty<OutboxEntry>();
			return Task.FromResult(entries);
		}

		public Task MarkDispatchedAsync(OutboxEntry entry, CancellationToken cancellationToken)
		{
			Assert.AreSame(_entry, entry);
			MarkedDispatched.TrySetResult();
			return Task.CompletedTask;
		}

		public Task ScheduleRetryAsync(
			OutboxEntry entry,
			string? errorMessage,
			DateTime nextAttemptAt,
			CancellationToken cancellationToken) => throw new AssertFailedException("Dispatch should not be retried.");

		public Task MarkPoisonedAsync(
			OutboxEntry entry,
			string? errorMessage,
			CancellationToken cancellationToken) => throw new AssertFailedException("Dispatch should not be poisoned.");

		public Task<long> GetPendingCountAsync(CancellationToken cancellationToken) => Task.FromResult(0L);

		public Task<long> GetPoisonedCountAsync(CancellationToken cancellationToken) => Task.FromResult(0L);
	}
}
