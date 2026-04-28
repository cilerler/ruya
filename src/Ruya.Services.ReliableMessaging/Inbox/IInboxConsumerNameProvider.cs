using System;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// Resolves the logical consumer identity from a handler type. The default implementation uses the type's
/// fully-qualified name; override by registering a replacement or by applying
/// <see cref="InboxConsumerNameAttribute"/> to the handler class.
/// </summary>
public interface IInboxConsumerNameProvider
{
	/// <summary>Returns the consumer name to use when recording inbox entries for <paramref name="handlerType"/>.</summary>
	string GetConsumerName(Type handlerType);
}
