using Microsoft.Extensions.DependencyInjection;

namespace Ruya.Services.ReliableMessaging.Extensions;

/// <summary>
/// Fluent builder returned by <c>AddReliableMessaging</c>. Adapter packages attach their registrations via
/// extension methods on this interface (e.g. <c>AddEntityFrameworkOutboxStore&lt;TDbContext&gt;</c>,
/// <c>AddMessageQueueOutboundDispatcher</c>).
/// </summary>
public interface IReliableMessagingBuilder
{
	/// <summary>Underlying service collection for adapter registrations.</summary>
	IServiceCollection Services { get; }
}
