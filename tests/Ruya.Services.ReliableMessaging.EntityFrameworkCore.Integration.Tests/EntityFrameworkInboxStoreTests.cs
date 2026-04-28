using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
