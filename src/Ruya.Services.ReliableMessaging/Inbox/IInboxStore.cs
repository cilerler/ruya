using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// Low-level storage primitives for manually orchestrated consumer-side deduplication. These methods do not by
/// themselves make the inbox claim, business mutation, and processed transition atomic; use
/// <see cref="IAtomicInboxStore{TContext}"/> for that guarantee.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context (typically the consumer's <c>DbContext</c>).</typeparam>
public interface IInboxStore<TContext>
{
	/// <summary>
	/// Attempts to record that <paramref name="consumerName"/> has received <paramref name="messageId"/>.
	/// Returns <see langword="true"/> when the row was inserted. <see langword="false"/> means only that a row already
	/// exists; it does not prove that associated business work completed or committed.
	/// </summary>
	Task<bool> TryRecordAsync(
		string consumerName,
		string messageId,
		string topic,
		CancellationToken cancellationToken);

	/// <summary>
	/// Marks a previously-recorded entry as <see cref="InboxStatus.Processed"/>. Manual callers that require atomic
	/// business processing must enlist this transition and their mutation in one transaction.
	/// </summary>
	Task MarkProcessedAsync(string consumerName, string messageId, CancellationToken cancellationToken);

	/// <summary>Deletes processed rows older than <paramref name="olderThanUtc"/>. Returns the number of rows removed.</summary>
	Task<int> CleanupProcessedAsync(System.DateTime olderThanUtc, CancellationToken cancellationToken);
}
