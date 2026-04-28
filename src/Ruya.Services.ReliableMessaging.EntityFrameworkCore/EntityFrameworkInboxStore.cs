using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Ruya.Services.ReliableMessaging.Inbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IInboxStore{TContext}"/>. Relies on the composite primary key
/// <c>(ConsumerName, MessageId)</c> for atomic dedup via insert-or-fail.
/// </summary>
/// <typeparam name="TDbContext">The concrete <see cref="DbContext"/> type that owns the inbox table.</typeparam>
public sealed class EntityFrameworkInboxStore<TDbContext> : IInboxStore<TDbContext>
	where TDbContext : DbContext
{
	private readonly TDbContext _context;

	public EntityFrameworkInboxStore(TDbContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		_context = context;
	}

	public async Task<bool> TryRecordAsync(
		string consumerName,
		string messageId,
		string topic,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(consumerName);
		ArgumentException.ThrowIfNullOrEmpty(messageId);
		ArgumentException.ThrowIfNullOrEmpty(topic);

		// Same-context fast path: if the composite key is already in the database (or in the change tracker),
		// it's a duplicate. EF Core throws InvalidOperationException (not DbUpdateException) when Add is called
		// with a key that's already tracked, so we skip straight to false rather than letting Add throw.
		var alreadyRecorded = await _context.Set<InboxEntry>()
			.AsNoTracking()
			.AnyAsync(e => e.ConsumerName == consumerName && e.MessageId == messageId, cancellationToken)
			.ConfigureAwait(false);
		if (alreadyRecorded)
		{
			return false;
		}

		var entry = new InboxEntry
		{
			ConsumerName = consumerName,
			MessageId = messageId,
			Topic = topic,
			ReceivedAt = DateTime.UtcNow,
			Status = InboxStatus.Received,
		};

		_context.Set<InboxEntry>().Add(entry);

		try
		{
			await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (DbUpdateException)
		{
			// Cross-instance race: another instance inserted between our check and SaveChanges.
			// The composite PK enforces dedup at the database level; detach so the context stays clean.
			_context.Entry(entry).State = EntityState.Detached;
			return false;
		}
	}

	public async Task MarkProcessedAsync(string consumerName, string messageId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(consumerName);
		ArgumentException.ThrowIfNullOrEmpty(messageId);

		var entry = await _context.Set<InboxEntry>()
			.FirstOrDefaultAsync(e => e.ConsumerName == consumerName && e.MessageId == messageId, cancellationToken)
			.ConfigureAwait(false);

		if (entry is null)
		{
			return; // nothing to mark; handler may not have taken the inbox path
		}

		entry.ProcessedAt = DateTime.UtcNow;
		entry.Status = InboxStatus.Processed;
		_context.Set<InboxEntry>().Update(entry);
		await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public Task<int> CleanupProcessedAsync(DateTime olderThanUtc, CancellationToken cancellationToken)
	{
		return _context.Set<InboxEntry>()
			.Where(e => e.Status == InboxStatus.Processed && e.ProcessedAt != null && e.ProcessedAt <= olderThanUtc)
			.ExecuteDeleteAsync(cancellationToken);
	}
}
