using System;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// Default provider: returns <c>typeof(handlerType).FullName</c>, or the class-level
/// <see cref="InboxConsumerNameAttribute"/> if one is applied. The attribute takes precedence over the convention.
/// </summary>
public sealed class TypeNameInboxConsumerNameProvider : IInboxConsumerNameProvider
{
	public string GetConsumerName(Type handlerType)
	{
		ArgumentNullException.ThrowIfNull(handlerType);

		var attribute = Attribute.GetCustomAttribute(handlerType, typeof(InboxConsumerNameAttribute)) as InboxConsumerNameAttribute;
		if (attribute is not null)
		{
			return attribute.Value;
		}

		return handlerType.FullName ?? handlerType.Name;
	}
}
