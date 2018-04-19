using System;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.RabbitMq;

namespace Ruya
{
	public static partial class StartupExtensions
	{
		public static IServiceCollection AddRabbitMq(this IServiceCollection serviceCollection)
		{
			if (serviceCollection == null)
			{
				throw new ArgumentNullException(nameof(serviceCollection));
			}

			serviceCollection.AddTransient<IMessageQueueSettings, MessageQueueSettings>();
			serviceCollection.AddTransient<Client>();
			return serviceCollection;
		}
	}
}
