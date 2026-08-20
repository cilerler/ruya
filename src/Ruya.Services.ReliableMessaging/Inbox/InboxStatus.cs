namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>Lifecycle state of an <see cref="InboxEntry"/>.</summary>
public enum InboxStatus : byte
{
	/// <summary>
	/// Message has been recorded inside an in-progress atomic attempt. A persisted <c>Received</c> row outside
	/// that transaction, including a row created by the legacy two-step flow, is ambiguous: it does not prove
	/// whether business work ran or committed and must be reconciled before replay or deduplication.
	/// </summary>
	Received = 0,

	/// <summary>
	/// Processing completed successfully. Atomic stores commit this transition with enlisted business changes;
	/// callers of the low-level Inbox API must provide their own transaction when they require that guarantee.
	/// </summary>
	Processed = 1,

	/// <summary>
	/// Processing was recorded as failed by a legacy or application-specific recovery path. The canonical atomic
	/// path rolls back instead of committing this state. Reconcile any persisted non-processed row before replay.
	/// </summary>
	Failed = 2,
}
