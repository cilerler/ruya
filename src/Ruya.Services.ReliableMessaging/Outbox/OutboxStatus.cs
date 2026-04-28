namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>Lifecycle state of an <see cref="OutboxEntry"/>.</summary>
public enum OutboxStatus : byte
{
	/// <summary>Awaiting dispatch. Eligible for the processor to pick up when <see cref="OutboxEntry.NextAttemptAt"/> is reached.</summary>
	Pending = 0,

	/// <summary>Successfully dispatched to the destination.</summary>
	Dispatched = 1,

	/// <summary>Exhausted retry attempts. Requires manual intervention.</summary>
	Poisoned = 2,
}
