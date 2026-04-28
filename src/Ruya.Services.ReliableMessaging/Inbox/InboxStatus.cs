namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>Lifecycle state of an <see cref="InboxEntry"/>.</summary>
public enum InboxStatus : byte
{
	/// <summary>Message has been recorded as received by the consumer. The handler may or may not have run yet.</summary>
	Received = 0,

	/// <summary>Handler completed successfully and the business work was committed in the same transaction.</summary>
	Processed = 1,

	/// <summary>Handler threw after recording. Row is retained for diagnosis; broker redelivery will re-dedup via PK.</summary>
	Failed = 2,
}
