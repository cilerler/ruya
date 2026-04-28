using System;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// Overrides the consumer name for a specific handler class. Takes precedence over
/// <see cref="IInboxConsumerNameProvider"/> implementations.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InboxConsumerNameAttribute : Attribute
{
	public InboxConsumerNameAttribute(string value)
	{
		ArgumentException.ThrowIfNullOrEmpty(value);
		Value = value;
	}

	public string Value { get; }
}
