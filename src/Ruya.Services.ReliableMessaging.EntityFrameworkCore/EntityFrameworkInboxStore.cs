using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Ruya.Services.ReliableMessaging.Inbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of the canonical <see cref="IAtomicInboxStore{TContext}"/> transaction and the low-level
/// <see cref="IInboxStore{TContext}"/> compatibility primitives. The composite <c>(ConsumerName, MessageId)</c> primary
/// key is the final cross-instance deduplication boundary.
/// </summary>
/// <typeparam name="TDbContext">The concrete <see cref="DbContext"/> type that owns the inbox table.</typeparam>
public sealed class EntityFrameworkInboxStore<TDbContext> : IInboxStore<TDbContext>, IAtomicInboxStore<TDbContext>
	where TDbContext : DbContext
{
	private readonly TDbContext _context;

	public EntityFrameworkInboxStore(TDbContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		_context = context;
	}

	public async Task<InboxExecutionResult> ExecuteOnceAsync(
		string consumerName,
		string messageId,
		string topic,
		Func<CancellationToken, Task<InboxWorkResult>> work,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(consumerName);
		ArgumentException.ThrowIfNullOrEmpty(messageId);
		ArgumentException.ThrowIfNullOrEmpty(topic);
		ArgumentNullException.ThrowIfNull(work);

		var strategy = _context.Database.CreateExecutionStrategy();
		return await strategy.ExecuteAsync(
			async ct =>
			{
				DbUpdateException? insertFailure = null;
				await using (var transaction = await _context.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
				{
					var transactionCompleted = false;

					try
					{
						var existingStatus = await GetStatusAsync(consumerName, messageId, ct).ConfigureAwait(false);
						if (existingStatus.HasValue)
						{
							if (existingStatus.Value != InboxStatus.Processed)
							{
								throw CreateAmbiguousEntryException(consumerName, messageId, topic, existingStatus.Value);
							}

							await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
							transactionCompleted = true;
							return InboxExecutionResult.Duplicate;
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
							await _context.SaveChangesAsync(ct).ConfigureAwait(false);
						}
						catch (DbUpdateException ex)
						{
							// A concurrent consumer may have inserted the same composite key after our initial read.
							// Resolve the persisted status after this transaction is fully disposed below. If no matching
							// row exists, the exception came from another constraint and remains eligible for strategy retry.
							await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
							transactionCompleted = true;
							_context.ChangeTracker.Clear();
							insertFailure = ex;
						}

						if (insertFailure is null)
						{
							var workResult = await work(ct).ConfigureAwait(false);
							switch (workResult)
							{
								case InboxWorkResult.Abandoned:
									await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
									transactionCompleted = true;
									_context.ChangeTracker.Clear();
									return InboxExecutionResult.Abandoned;

								case InboxWorkResult.Processed:
									entry.ProcessedAt = DateTime.UtcNow;
									entry.Status = InboxStatus.Processed;
									await _context.SaveChangesAsync(ct).ConfigureAwait(false);
									await transaction.CommitAsync(ct).ConfigureAwait(false);
									transactionCompleted = true;
									return InboxExecutionResult.Processed;

								default:
									throw new InvalidOperationException($"Unsupported inbox work result '{workResult}'.");
							}
						}
					}
					catch (Exception exception)
					{
						try
						{
							if (!transactionCompleted)
							{
								try
								{
									await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
								}
								catch (InvalidOperationException rollbackException)
								{
									// Preserve the original exception so the execution strategy can classify and retry it.
									exception.Data["AtomicInboxRollbackException"] = rollbackException;
								}
								catch (DbException rollbackException)
								{
									// Preserve the original exception so the execution strategy can classify and retry it.
									exception.Data["AtomicInboxRollbackException"] = rollbackException;
								}
							}
						}
						finally
						{
							_context.ChangeTracker.Clear();
						}

						throw;
					}
				}

				var conflictingStatus = await GetStatusAsync(consumerName, messageId, ct).ConfigureAwait(false);
				if (conflictingStatus == InboxStatus.Processed)
				{
					return InboxExecutionResult.Duplicate;
				}

				if (conflictingStatus.HasValue)
				{
					throw CreateAmbiguousEntryException(
						consumerName,
						messageId,
						topic,
						conflictingStatus.Value,
						insertFailure);
				}

				if (insertFailure is not null)
				{
					throw insertFailure;
				}

				throw new InvalidOperationException("Atomic inbox execution completed without a result.");
			},
			cancellationToken).ConfigureAwait(false);
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

	private Task<InboxStatus?> GetStatusAsync(
		string consumerName,
		string messageId,
		CancellationToken cancellationToken)
	{
		return _context.Set<InboxEntry>()
			.AsNoTracking()
			.Where(e => e.ConsumerName == consumerName && e.MessageId == messageId)
			.Select(e => (InboxStatus?)e.Status)
			.SingleOrDefaultAsync(cancellationToken);
	}

	private static InvalidOperationException CreateAmbiguousEntryException(
		string consumerName,
		string messageId,
		string topic,
		InboxStatus status,
		Exception? innerException = null)
	{
		var message =
			$"Inbox entry for consumer '{consumerName}', message '{messageId}', and topic '{topic}' " +
			$"is in non-processed status '{status}'. Atomic processing cannot determine whether the " +
			"associated business mutation committed. An operator must reconcile the inbox entry and " +
			"business state before this message can be processed again.";

		return innerException is null
			? new InvalidOperationException(message)
			: new InvalidOperationException(message, innerException);
	}
}
