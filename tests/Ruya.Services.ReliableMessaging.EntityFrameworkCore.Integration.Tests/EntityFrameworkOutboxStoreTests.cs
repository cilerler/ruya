using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore.Integration.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class EntityFrameworkOutboxStoreTests : IntegrationTestBase
{
	private EntityFrameworkOutboxStore<TestDbContext> _store = null!;

	[TestInitialize]
	public async Task InitAsync()
	{
		await InitializeAsync();
		_store = new EntityFrameworkOutboxStore<TestDbContext>(Db);
	}

	[TestCleanup]
	public async Task CleanupAsyncWrap() => await CleanupAsync();

	[TestMethod]
	public async Task FetchPendingAsync_WithNoEntries_ReturnsEmpty()
	{
		var result = await _store.FetchPendingAsync(10, CancellationToken.None);

		Assert.AreEqual(0, result.Count);
	}

	[TestMethod]
	public async Task FetchPendingAsync_OnlyReturnsPendingEntriesEligibleByNextAttemptAt()
	{
		var now = DateTime.UtcNow;

		Db.Outbox.AddRange(
			NewEntry("p1", OutboxStatus.Pending, now.AddSeconds(-10)),
			NewEntry("p2", OutboxStatus.Pending, now.AddSeconds(10)),   // not yet due
			NewEntry("d1", OutboxStatus.Dispatched, now.AddSeconds(-20)),
			NewEntry("x1", OutboxStatus.Poisoned, now.AddSeconds(-20)));
		await Db.SaveChangesAsync();

		var result = await _store.FetchPendingAsync(10, CancellationToken.None);

		Assert.AreEqual(1, result.Count);
		Assert.AreEqual("p1", result[0].Topic);
	}

	[TestMethod]
	public async Task FetchPendingAsync_OrdersByEnqueuedAtAscending()
	{
		var baseTime = DateTime.UtcNow.AddMinutes(-10);

		Db.Outbox.AddRange(
			NewEntry("third", OutboxStatus.Pending, DateTime.UtcNow, enqueuedAt: baseTime.AddSeconds(30)),
			NewEntry("first", OutboxStatus.Pending, DateTime.UtcNow, enqueuedAt: baseTime.AddSeconds(10)),
			NewEntry("second", OutboxStatus.Pending, DateTime.UtcNow, enqueuedAt: baseTime.AddSeconds(20)));
		await Db.SaveChangesAsync();

		var result = await _store.FetchPendingAsync(10, CancellationToken.None);

		Assert.AreEqual(3, result.Count);
		Assert.AreEqual("first", result[0].Topic);
		Assert.AreEqual("second", result[1].Topic);
		Assert.AreEqual("third", result[2].Topic);
	}

	[TestMethod]
	public async Task MarkDispatchedAsync_SetsDispatchedStatusAndTimestamp()
	{
		var entry = NewEntry("topic.x", OutboxStatus.Pending, DateTime.UtcNow.AddSeconds(-1));
		Db.Outbox.Add(entry);
		await Db.SaveChangesAsync();
		Db.ChangeTracker.Clear();

		var fetched = (await _store.FetchPendingAsync(1, CancellationToken.None)).Single();
		await _store.MarkDispatchedAsync(fetched, CancellationToken.None);

		var stored = await Db.Outbox.AsNoTracking().FirstAsync(e => e.Id == fetched.Id);
		Assert.AreEqual(OutboxStatus.Dispatched, stored.Status);
		Assert.IsNotNull(stored.DispatchedAt);
		Assert.IsNull(stored.LastError);
	}

	[TestMethod]
	public async Task ScheduleRetryAsync_IncrementsAttemptAndSetsNextTimeAndError()
	{
		var entry = NewEntry("topic.x", OutboxStatus.Pending, DateTime.UtcNow.AddSeconds(-1));
		Db.Outbox.Add(entry);
		await Db.SaveChangesAsync();
		Db.ChangeTracker.Clear();

		var fetched = (await _store.FetchPendingAsync(1, CancellationToken.None)).Single();
		var retryAt = DateTime.UtcNow.AddMinutes(5);
		await _store.ScheduleRetryAsync(fetched, "broker unavailable", retryAt, CancellationToken.None);

		var stored = await Db.Outbox.AsNoTracking().FirstAsync(e => e.Id == fetched.Id);
		Assert.AreEqual(1, stored.AttemptCount);
		Assert.AreEqual("broker unavailable", stored.LastError);
		Assert.IsTrue((stored.NextAttemptAt - retryAt).Duration() < TimeSpan.FromSeconds(1));
		Assert.AreEqual(OutboxStatus.Pending, stored.Status);
	}

	[TestMethod]
	public async Task MarkPoisonedAsync_SetsPoisonedStatusAndPreservesError()
	{
		var entry = NewEntry("topic.x", OutboxStatus.Pending, DateTime.UtcNow.AddSeconds(-1));
		Db.Outbox.Add(entry);
		await Db.SaveChangesAsync();
		Db.ChangeTracker.Clear();

		var fetched = (await _store.FetchPendingAsync(1, CancellationToken.None)).Single();
		await _store.MarkPoisonedAsync(fetched, "too many failures", CancellationToken.None);

		var stored = await Db.Outbox.AsNoTracking().FirstAsync(e => e.Id == fetched.Id);
		Assert.AreEqual(OutboxStatus.Poisoned, stored.Status);
		Assert.AreEqual("too many failures", stored.LastError);
	}

	[TestMethod]
	public async Task GetPendingCountAsync_ReturnsCountOfPendingEntries()
	{
		var now = DateTime.UtcNow;
		Db.Outbox.AddRange(
			NewEntry("a", OutboxStatus.Pending, now),
			NewEntry("b", OutboxStatus.Pending, now),
			NewEntry("c", OutboxStatus.Dispatched, now),
			NewEntry("d", OutboxStatus.Poisoned, now));
		await Db.SaveChangesAsync();

		var count = await _store.GetPendingCountAsync(CancellationToken.None);

		Assert.AreEqual(2, count);
	}

	[TestMethod]
	public async Task GetPoisonedCountAsync_ReturnsCountOfPoisonedEntries()
	{
		var now = DateTime.UtcNow;
		Db.Outbox.AddRange(
			NewEntry("a", OutboxStatus.Poisoned, now),
			NewEntry("b", OutboxStatus.Pending, now),
			NewEntry("c", OutboxStatus.Poisoned, now));
		await Db.SaveChangesAsync();

		var count = await _store.GetPoisonedCountAsync(CancellationToken.None);

		Assert.AreEqual(2, count);
	}

	private static OutboxEntry NewEntry(
		string topic,
		OutboxStatus status,
		DateTime nextAttemptAt,
		DateTime? enqueuedAt = null)
	{
		return new OutboxEntry
		{
			Id = Guid.NewGuid(),
			Topic = topic,
			PayloadJson = "{}",
			PayloadType = typeof(object).AssemblyQualifiedName!,
			EnqueuedAt = enqueuedAt ?? DateTime.UtcNow,
			NextAttemptAt = nextAttemptAt,
			Status = status,
		};
	}
}
