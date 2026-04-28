using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ruya.Services.ReliableMessaging.Extensions;

namespace Ruya.Services.ReliableMessaging.MessageQueue.Extensions;

/// <summary>DI registration surface for the Ruya.Services.MessageQueue adapter.</summary>
public static class StartupExtensions
{
	/// <summary>
	/// Registers <see cref="MessageQueueOutboundDispatcher"/> as the <see cref="IOutboundDispatcher"/> implementation.
	/// The dispatcher uses <c>IMessageQueueFactory</c> (from <c>Ruya.Services.MessageQueue</c>) to resolve a named
	/// queue and then forwards outbox envelopes via <c>PublishAsync</c>.
	/// </summary>
	public static IReliableMessagingBuilder AddMessageQueueOutboundDispatcher(
		this IReliableMessagingBuilder builder,
		Action<MessageQueueDispatcherOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		var optionsBuilder = builder.Services.AddOptions<MessageQueueDispatcherOptions>()
			.BindConfiguration(MessageQueueDispatcherOptions.ConfigurationSectionName);

		if (configure is not null)
		{
			optionsBuilder.Configure(configure);
		}

		builder.Services.TryAddSingleton<IOutboundDispatcher, MessageQueueOutboundDispatcher>();

		return builder;
	}
}
