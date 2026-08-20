using System;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>Consumer-side options governing the inbox store and cleanup behaviour.</summary>
public sealed class InboxOptions
{
	/// <summary>
	/// How long processed rows are retained before cleanup. <see cref="TimeSpan.Zero"/> disables automatic cleanup.
	/// Rows must be retained long enough that any realistic broker-side redelivery (retry backoff, restart) still
	/// sees the entry — otherwise a duplicate will be processed again.
	/// </summary>
	public TimeSpan ArchiveAfter { get; set; } = TimeSpan.FromDays(7);

	/// <summary>
	/// Retained for configuration compatibility with manually orchestrated <c>IInboxStore</c> flows.
	/// The canonical atomic Inbox path does not consult this value: it commits <c>Processed</c> on success
	/// and rolls back abandoned work or exceptions. The MessageQueue adapter maps retry and rejection to
	/// abandoned work. Manual callers own any explicit completion policy.
	/// </summary>
	public bool RequireExplicitProcessed { get; set; }

	/// <summary>How often the cleanup processor runs.</summary>
	public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
