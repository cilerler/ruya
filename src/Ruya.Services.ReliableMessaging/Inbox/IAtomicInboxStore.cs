using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// Coordinates inbox deduplication and business work as one atomic operation.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context.</typeparam>
public interface IAtomicInboxStore<TContext>
{
	/// <summary>
	/// Executes <paramref name="work"/> only when the message has not already completed successfully.
	/// Implementations commit the inbox record only when the work returns <see cref="InboxWorkResult.Processed"/>.
	/// Returning <see cref="InboxWorkResult.Abandoned"/> or throwing rolls back changes made by that execution,
	/// including its new inbox record and any business changes enlisted in the same transaction.
	/// </summary>
	/// <remarks>
	/// A persistence implementation may invoke <paramref name="work"/> more than once when its execution strategy
	/// retries a transient database failure. Work should therefore remain transaction-bound or otherwise idempotent.
	/// External side effects cannot participate in the database rollback and require their own idempotency key or a
	/// transactional Outbox.
	/// </remarks>
	Task<InboxExecutionResult> ExecuteOnceAsync(
		string consumerName,
		string messageId,
		string topic,
		Func<CancellationToken, Task<InboxWorkResult>> work,
		CancellationToken cancellationToken);
}

/// <summary>Outcome reported by work running inside an atomic inbox operation.</summary>
public enum InboxWorkResult
{
	/// <summary>Business work completed and should be committed with the inbox record.</summary>
	Processed = 0,

	/// <summary>Business work did not complete and should be rolled back for a later attempt.</summary>
	Abandoned = 1,
}

/// <summary>Result of an atomic inbox execution attempt.</summary>
public enum InboxExecutionResult
{
	/// <summary>The work and inbox record committed successfully.</summary>
	Processed = 0,

	/// <summary>A previously committed processed record already exists; the work was not invoked.</summary>
	Duplicate = 1,

	/// <summary>The work declined completion and all changes were rolled back.</summary>
	Abandoned = 2,
}
