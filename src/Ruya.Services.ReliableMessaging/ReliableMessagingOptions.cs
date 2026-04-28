using Ruya.Services.ReliableMessaging.Inbox;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging;

/// <summary>
/// Root options for the reliable messaging pair. Bind under the <see cref="ConfigurationSectionName"/> section
/// to override defaults; individual sub-sections are bindable independently.
/// </summary>
public sealed class ReliableMessagingOptions
{
	public const string ConfigurationSectionName = "ReliableMessaging";

	/// <summary>Producer-side options.</summary>
	public OutboxOptions Outbox { get; set; } = new();

	/// <summary>Consumer-side options.</summary>
	public InboxOptions Inbox { get; set; } = new();
}
