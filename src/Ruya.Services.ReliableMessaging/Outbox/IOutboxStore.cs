using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>
/// Storage contract for outbox entries for a specific persistence context.
/// Implementations (e.g. <c>EntityFrameworkOutboxStore&lt;TDbContext&gt;</c>) handle the actual persistence mechanism.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context (typically the caller's <c>DbContext</c>).</typeparam>
public interface IOutboxStore<TContext>
{
	/// <summary>
	/// Fetches up to <paramref name="batchSize"/> entries that are eligible for dispatch
	/// (i.e., <see cref="OutboxStatus.Pending"/> and <see cref="OutboxEntry.NextAttemptAt"/> <c>&lt;= UtcNow</c>).
	/// Implementations may apply locking hints (e.g. SQL Server <c>UPDLOCK, READPAST</c>) to support horizontal scaling.
	/// </summary>
	Task<IReadOnlyList<OutboxEntry>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken);

	/// <summary>Marks an entry as successfully dispatched. Idempotent.</summary>
	Task MarkDispatchedAsync(OutboxEntry entry, CancellationToken cancellationToken);

	/// <summary>Records a dispatch failure, increments attempt count, and schedules the next retry.</summary>
	Task ScheduleRetryAsync(OutboxEntry entry, string? errorMessage, System.DateTime nextAttemptAt, CancellationToken cancellationToken);

	/// <summary>Marks an entry as <see cref="OutboxStatus.Poisoned"/> after exhausting retries.</summary>
	Task MarkPoisonedAsync(OutboxEntry entry, string? errorMessage, CancellationToken cancellationToken);

	/// <summary>
	/// Count of entries currently in <see cref="OutboxStatus.Pending"/> state. Used by health checks.
	/// </summary>
	Task<long> GetPendingCountAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Count of entries currently in <see cref="OutboxStatus.Poisoned"/> state. Used by health checks.
	/// </summary>
	Task<long> GetPoisonedCountAsync(CancellationToken cancellationToken);
}
