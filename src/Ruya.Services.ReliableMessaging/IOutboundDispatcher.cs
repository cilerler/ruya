using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.ReliableMessaging;

/// <summary>
/// Dispatches a persisted <see cref="ReliableMessageEnvelope"/> to its final destination (message broker, HTTP, etc.).
/// Implementations live outside this package (e.g. Ruya.Services.ReliableMessaging.MessageQueue).
/// </summary>
public interface IOutboundDispatcher
{
	/// <summary>
	/// Sends the envelope to its final destination. Must be idempotent on the caller's side — the processor may invoke
	/// this multiple times for the same envelope under retry scenarios.
	/// </summary>
	Task DispatchAsync(ReliableMessageEnvelope envelope, CancellationToken cancellationToken = default);
}
