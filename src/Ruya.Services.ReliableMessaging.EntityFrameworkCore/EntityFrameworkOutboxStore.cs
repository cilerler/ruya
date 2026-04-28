using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IOutboxStore{TContext}"/>.
/// Operates on the caller's <typeparamref name="TDbContext"/> — the <see cref="OutboxEntry"/> entity must be mapped
/// via <see cref="ModelBuilderExtensions.ApplyOutboxEntryConfiguration"/> in <c>OnModelCreating</c>.
/// </summary>
/// <typeparam name="TDbContext">The concrete <see cref="DbContext"/> type that owns the outbox table.</typeparam>
public sealed class EntityFrameworkOutboxStore<TDbContext> : IOutboxStore<TDbContext>
	where TDbContext : DbContext
{
	private readonly TDbContext _context;

	public EntityFrameworkOutboxStore(TDbContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		_context = context;
	}

	public async Task<IReadOnlyList<OutboxEntry>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		var entries = await _context.Set<OutboxEntry>()
			.Where(e => e.Status == OutboxStatus.Pending && e.NextAttemptAt <= now)
			.OrderBy(e => e.EnqueuedAt)
			.Take(batchSize)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return entries;
	}

	public async Task MarkDispatchedAsync(OutboxEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		entry.Status = OutboxStatus.Dispatched;
		entry.DispatchedAt = DateTime.UtcNow;
		entry.LastError = null;
		_context.Set<OutboxEntry>().Update(entry);
		await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task ScheduleRetryAsync(
		OutboxEntry entry,
		string? errorMessage,
		DateTime nextAttemptAt,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		entry.AttemptCount += 1;
		entry.NextAttemptAt = nextAttemptAt;
		entry.LastError = errorMessage;
		_context.Set<OutboxEntry>().Update(entry);
		await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task MarkPoisonedAsync(OutboxEntry entry, string? errorMessage, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);

		entry.AttemptCount += 1;
		entry.Status = OutboxStatus.Poisoned;
		entry.LastError = errorMessage;
		_context.Set<OutboxEntry>().Update(entry);
		await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public Task<long> GetPendingCountAsync(CancellationToken cancellationToken)
	{
		return _context.Set<OutboxEntry>()
			.LongCountAsync(e => e.Status == OutboxStatus.Pending, cancellationToken);
	}

	public Task<long> GetPoisonedCountAsync(CancellationToken cancellationToken)
	{
		return _context.Set<OutboxEntry>()
			.LongCountAsync(e => e.Status == OutboxStatus.Poisoned, cancellationToken);
	}
}
