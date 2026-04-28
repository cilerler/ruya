using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// Storage contract for consumer-side idempotency. Implementations rely on the composite
/// <c>(ConsumerName, MessageId)</c> primary key for atomic dedup via insert-or-fail.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context (typically the consumer's <c>DbContext</c>).</typeparam>
public interface IInboxStore<TContext>
{
	/// <summary>
	/// Attempts to record that <paramref name="consumerName"/> has received <paramref name="messageId"/>.
	/// Returns <see langword="true"/> on first-seen (the row was inserted); <see langword="false"/> on duplicate.
	/// </summary>
	Task<bool> TryRecordAsync(
		string consumerName,
		string messageId,
		string topic,
		CancellationToken cancellationToken);

	/// <summary>
	/// Marks a previously-recorded entry as <see cref="InboxStatus.Processed"/>. Optional unless
	/// <see cref="InboxOptions.RequireExplicitProcessed"/> is enabled.
	/// </summary>
	Task MarkProcessedAsync(string consumerName, string messageId, CancellationToken cancellationToken);

	/// <summary>Deletes processed rows older than <paramref name="olderThanUtc"/>. Returns the number of rows removed.</summary>
	Task<int> CleanupProcessedAsync(System.DateTime olderThanUtc, CancellationToken cancellationToken);
}
