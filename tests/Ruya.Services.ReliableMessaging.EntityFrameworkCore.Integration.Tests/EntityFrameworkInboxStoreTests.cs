using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.ReliableMessaging.EntityFrameworkCore.Extensions;
using Ruya.Services.ReliableMessaging.Extensions;
using Ruya.Services.ReliableMessaging.Inbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore.Integration.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class EntityFrameworkInboxStoreTests : IntegrationTestBase
{
	private EntityFrameworkInboxStore<TestDbContext> _store = null!;

	[TestInitialize]
	public async Task InitAsync()
	{
		await InitializeAsync();
		_store = new EntityFrameworkInboxStore<TestDbContext>(Db);
	}

	[TestCleanup]
	public async Task CleanupAsyncWrap() => await CleanupAsync();

	[TestMethod]
	public async Task TryRecordAsync_FirstTime_ReturnsTrueAndPersistsRow()
	{
		var recorded = await _store.TryRecordAsync("consumer.a", "msg-1", "topic.x", CancellationToken.None);

		Assert.IsTrue(recorded);
		var stored = await Db.Inbox.AsNoTracking()
			.FirstAsync(e => e.ConsumerName == "consumer.a" && e.MessageId == "msg-1");
		Assert.AreEqual("topic.x", stored.Topic);
		Assert.AreEqual(InboxStatus.Received, stored.Status);
	}

	[TestMethod]
	public async Task TryRecordAsync_DuplicateSameConsumer_ReturnsFalse()
	{
		await _store.TryRecordAsync("consumer.a", "msg-1", "topic.x", CancellationToken.None);

		var second = await _store.TryRecordAsync("consumer.a", "msg-1", "topic.x", CancellationToken.None);

		Assert.IsFalse(second);
		Assert.AreEqual(1, await Db.Inbox.AsNoTracking().CountAsync());
	}

	[TestMethod]
	public async Task TryRecordAsync_SameMessageDifferentConsumers_BothReturnTrue()
	{
		var first = await _store.TryRecordAsync("consumer.a", "msg-1", "topic.x", CancellationToken.None);
		var second = await _store.TryRecordAsync("consumer.b", "msg-1", "topic.x", CancellationToken.None);

		Assert.IsTrue(first);
		Assert.IsTrue(second);
		Assert.AreEqual(2, await Db.Inbox.AsNoTracking().CountAsync());
	}

	[TestMethod]
	public async Task MarkProcessedAsync_ExistingEntry_SetsProcessedStatusAndTimestamp()
	{
		await _store.TryRecordAsync("consumer.a", "msg-1", "topic.x", CancellationToken.None);
		Db.ChangeTracker.Clear();

		await _store.MarkProcessedAsync("consumer.a", "msg-1", CancellationToken.None);

		var stored = await Db.Inbox.AsNoTracking()
			.FirstAsync(e => e.ConsumerName == "consumer.a" && e.MessageId == "msg-1");
		Assert.AreEqual(InboxStatus.Processed, stored.Status);
		Assert.IsNotNull(stored.ProcessedAt);
	}

	[TestMethod]
	public async Task MarkProcessedAsync_MissingEntry_DoesNothing()
	{
		await _store.MarkProcessedAsync("consumer.x", "does-not-exist", CancellationToken.None);

		Assert.AreEqual(0, await Db.Inbox.AsNoTracking().CountAsync());
	}

	[TestMethod]
	public async Task ExecuteOnceAsync_AbandonedWork_RollsBackBusinessAndInbox_ThenProcessedRetryCommitsOnce()
	{
		var callbackInvocations = 0;

		var abandoned = await _store.ExecuteOnceAsync(
			"consumer.atomic",
			"msg-abandoned",
			"topic.atomic",
			async cancellationToken =>
			{
				callbackInvocations++;
				Assert.IsNotNull(Db.Database.CurrentTransaction);
				Db.BusinessRecords.Add(new TestBusinessRecord { Id = 1, Value = "abandoned" });
				await Db.SaveChangesAsync(cancellationToken);
				return InboxWorkResult.Abandoned;
			},
			CancellationToken.None);

		Assert.AreEqual(InboxExecutionResult.Abandoned, abandoned);
		Assert.AreEqual(1, callbackInvocations);
		Assert.AreEqual(0, await Db.BusinessRecords.AsNoTracking().CountAsync());
		Assert.AreEqual(0, await Db.Inbox.AsNoTracking().CountAsync());

		var processed = await _store.ExecuteOnceAsync(
			"consumer.atomic",
			"msg-abandoned",
			"topic.atomic",
			async cancellationToken =>
			{
				callbackInvocations++;
				Assert.IsNotNull(Db.Database.CurrentTransaction);
				Db.BusinessRecords.Add(new TestBusinessRecord { Id = 1, Value = "processed" });
				await Db.SaveChangesAsync(cancellationToken);
				return InboxWorkResult.Processed;
			},
			CancellationToken.None);

		Assert.AreEqual(InboxExecutionResult.Processed, processed);
		Assert.AreEqual(2, callbackInvocations);
		Assert.AreEqual(1, await Db.BusinessRecords.AsNoTracking().CountAsync());
		Assert.AreEqual("processed", await Db.BusinessRecords.AsNoTracking().Select(record => record.Value).SingleAsync());
		var inbox = await Db.Inbox.AsNoTracking().SingleAsync();
		Assert.AreEqual(InboxStatus.Processed, inbox.Status);
		Assert.IsNotNull(inbox.ProcessedAt);
	}

	[TestMethod]
	public async Task ExecuteOnceAsync_ThrownWork_RollsBackBusinessAndInbox_AndRemainsEligible()
	{
		var callbackInvocations = 0;

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
			await _store.ExecuteOnceAsync(
				"consumer.atomic",
				"msg-thrown",
				"topic.atomic",
				async cancellationToken =>
				{
					callbackInvocations++;
					Assert.IsNotNull(Db.Database.CurrentTransaction);
					Db.BusinessRecords.Add(new TestBusinessRecord { Id = 2, Value = "rolled-back" });
					await Db.SaveChangesAsync(cancellationToken);
					throw new InvalidOperationException("Work failed.");
				},
				CancellationToken.None));

		Assert.AreEqual(1, callbackInvocations);
		Assert.AreEqual(0, await Db.BusinessRecords.AsNoTracking().CountAsync());
		Assert.AreEqual(0, await Db.Inbox.AsNoTracking().CountAsync());

		var retry = await _store.ExecuteOnceAsync(
			"consumer.atomic",
			"msg-thrown",
			"topic.atomic",
			async cancellationToken =>
			{
				callbackInvocations++;
				Db.BusinessRecords.Add(new TestBusinessRecord { Id = 2, Value = "retried" });
				await Db.SaveChangesAsync(cancellationToken);
				return InboxWorkResult.Processed;
			},
			CancellationToken.None);

		Assert.AreEqual(InboxExecutionResult.Processed, retry);
		Assert.AreEqual(2, callbackInvocations);
		Assert.AreEqual("retried", await Db.BusinessRecords.AsNoTracking().Select(record => record.Value).SingleAsync());
		Assert.AreEqual(InboxStatus.Processed, (await Db.Inbox.AsNoTracking().SingleAsync()).Status);
	}

	[TestMethod]
	public async Task ExecuteOnceAsync_CommittedProcessedDuplicate_SkipsCallback()
	{
		var callbackInvocations = 0;

		var first = await _store.ExecuteOnceAsync(
			"consumer.atomic",
			"msg-duplicate",
			"topic.atomic",
			async cancellationToken =>
			{
				callbackInvocations++;
				Db.BusinessRecords.Add(new TestBusinessRecord { Id = 3, Value = "once" });
				await Db.SaveChangesAsync(cancellationToken);
				return InboxWorkResult.Processed;
			},
			CancellationToken.None);

		var duplicate = await _store.ExecuteOnceAsync(
			"consumer.atomic",
			"msg-duplicate",
			"topic.atomic",
			_ =>
			{
				callbackInvocations++;
				return Task.FromResult(InboxWorkResult.Processed);
			},
			CancellationToken.None);

		Assert.AreEqual(InboxExecutionResult.Processed, first);
		Assert.AreEqual(InboxExecutionResult.Duplicate, duplicate);
		Assert.AreEqual(1, callbackInvocations);
		Assert.AreEqual(1, await Db.BusinessRecords.AsNoTracking().CountAsync());
		Assert.AreEqual(1, await Db.Inbox.AsNoTracking().CountAsync());
	}

	[TestMethod]
	public async Task ExecuteOnceAsync_ConcurrentIndependentContexts_InvokesOneCallbackAndCommitsOnce()
	{
		var databasePath = Path.Combine(Path.GetTempPath(), $"ruya-inbox-{Guid.NewGuid():N}.db");
		var connectionString = $"Data Source={databasePath};Default Timeout=10;Pooling=False";

		try
		{
			var options = new DbContextOptionsBuilder<TestDbContext>()
				.UseSqlite(connectionString)
				.Options;

			await using (var setup = new TestDbContext(options))
			{
				await setup.Database.EnsureCreatedAsync();
			}

			await using var firstContext = new TestDbContext(options);
			await using var secondContext = new TestDbContext(options);
			var firstStore = new EntityFrameworkInboxStore<TestDbContext>(firstContext);
			var secondStore = new EntityFrameworkInboxStore<TestDbContext>(secondContext);
			var callbackInvocations = 0;

			Task<InboxExecutionResult> ExecuteAsync(
				EntityFrameworkInboxStore<TestDbContext> store,
				TestDbContext context) =>
				store.ExecuteOnceAsync(
					"consumer.concurrent",
					"msg-concurrent",
					"topic.concurrent",
					async cancellationToken =>
					{
						Interlocked.Increment(ref callbackInvocations);
						context.BusinessRecords.Add(new TestBusinessRecord { Id = 90, Value = "committed-once" });
						await context.SaveChangesAsync(cancellationToken);
						await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
						return InboxWorkResult.Processed;
					},
					CancellationToken.None);

			var results = await Task.WhenAll(
				ExecuteAsync(firstStore, firstContext),
				ExecuteAsync(secondStore, secondContext));

			Assert.AreEqual(1, results.Count(result => result == InboxExecutionResult.Processed));
			Assert.AreEqual(1, results.Count(result => result == InboxExecutionResult.Duplicate));
			Assert.AreEqual(1, callbackInvocations);

			await using var verification = new TestDbContext(options);
			Assert.AreEqual(1, await verification.BusinessRecords.AsNoTracking().CountAsync());
			Assert.AreEqual(1, await verification.Inbox.AsNoTracking().CountAsync());
			Assert.AreEqual(InboxStatus.Processed, (await verification.Inbox.AsNoTracking().SingleAsync()).Status);
		}
		finally
		{
			File.Delete(databasePath);
		}
	}

	[TestMethod]
	[DataRow(InboxStatus.Received)]
	[DataRow(InboxStatus.Failed)]
	public async Task ExecuteOnceAsync_LegacyIncompleteEntry_RequiresReconciliation(InboxStatus status)
	{
		Db.Inbox.Add(new InboxEntry
		{
			ConsumerName = "consumer.legacy",
			MessageId = $"msg-{status}",
			Topic = "topic.legacy",
			ReceivedAt = DateTime.UtcNow,
			Status = status,
		});
		await Db.SaveChangesAsync();
		Db.ChangeTracker.Clear();
		var callbackInvoked = false;

		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
			await _store.ExecuteOnceAsync(
				"consumer.legacy",
				$"msg-{status}",
				"topic.legacy",
				_ =>
				{
					callbackInvoked = true;
					return Task.FromResult(InboxWorkResult.Processed);
				},
				CancellationToken.None));

		Assert.IsFalse(callbackInvoked);
		StringAssert.Contains(exception.Message, "operator must reconcile", StringComparison.Ordinal);
		Assert.AreEqual(1, await Db.Inbox.AsNoTracking().CountAsync());
	}

	[TestMethod]
	public async Task AddEntityFrameworkInboxStore_ExposesOneScopedStoreThroughBothContracts()
	{
		var services = new ServiceCollection();
		services.AddDbContext<TestDbContext>(options => options.UseSqlite("Data Source=:memory:"));
		services
			.AddReliableMessaging()
			.AddEntityFrameworkInboxStore<TestDbContext>();

		await using var provider = services.BuildServiceProvider();
		await using var scope = provider.CreateAsyncScope();
		var concrete = scope.ServiceProvider.GetRequiredService<EntityFrameworkInboxStore<TestDbContext>>();

		Assert.AreSame(concrete, scope.ServiceProvider.GetRequiredService<IInboxStore<TestDbContext>>());
		Assert.AreSame(concrete, scope.ServiceProvider.GetRequiredService<IAtomicInboxStore<TestDbContext>>());
	}

	[TestMethod]
	public async Task CleanupProcessedAsync_DeletesOldProcessedRows()
	{
		var now = DateTime.UtcNow;

		Db.Inbox.AddRange(
			new InboxEntry
			{
				ConsumerName = "c.a",
				MessageId = "old-1",
				Topic = "t",
				ReceivedAt = now.AddDays(-10),
				ProcessedAt = now.AddDays(-10),
				Status = InboxStatus.Processed,
			},
			new InboxEntry
			{
				ConsumerName = "c.a",
				MessageId = "new-1",
				Topic = "t",
				ReceivedAt = now.AddHours(-1),
				ProcessedAt = now.AddHours(-1),
				Status = InboxStatus.Processed,
			},
			new InboxEntry
			{
				ConsumerName = "c.a",
				MessageId = "still-received",
				Topic = "t",
				ReceivedAt = now.AddDays(-10),
				ProcessedAt = null,
				Status = InboxStatus.Received,
			});
		await Db.SaveChangesAsync();

		var threshold = now.AddDays(-5);
		var removed = await _store.CleanupProcessedAsync(threshold, CancellationToken.None);

		Assert.AreEqual(1, removed);
		var remaining = await Db.Inbox.AsNoTracking().Select(e => e.MessageId).ToListAsync();
		CollectionAssert.AreEquivalent(new[] { "new-1", "still-received" }, remaining);
	}
}
