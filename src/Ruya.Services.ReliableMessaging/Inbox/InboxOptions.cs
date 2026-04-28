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
	/// When <see langword="true"/>, handlers must explicitly call <c>IInboxStore.MarkProcessedAsync</c>.
	/// When <see langword="false"/> (default), a successful handler return is treated as implicit success.
	/// </summary>
	public bool RequireExplicitProcessed { get; set; }

	/// <summary>How often the cleanup processor runs.</summary>
	public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
