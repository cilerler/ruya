namespace Ruya.Services.ReliableMessaging.MessageQueue;

/// <summary>Options for <see cref="MessageQueueOutboundDispatcher"/>.</summary>
public sealed class MessageQueueDispatcherOptions
{
	public const string ConfigurationSectionName = "ReliableMessaging:MessageQueueDispatcher";

	/// <summary>
	/// Name used when calling <see cref="Ruya.Services.MessageQueue.Abstractions.IMessageQueueFactory.CreateQueueAsync"/>
	/// and the outbox envelope does not specify a <see cref="ReliableMessageEnvelope.DispatcherName"/>.
	/// Typically matches a provider key in <c>MessageQueue:Providers</c>.
	/// </summary>
	public string QueueName { get; set; } = "default";
}
