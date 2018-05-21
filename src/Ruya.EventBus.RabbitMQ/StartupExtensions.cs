using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ruya.EventBus.Abstractions;
using Ruya.EventBus.RabbitMQ;

// ReSharper disable once CheckNamespace
namespace Ruya
{
    public static partial class StartupExtensions
    {
        public static IServiceCollection AddEventBusRabbitMq(this IServiceCollection serviceCollection, IConfiguration configuration)
        {
            if (serviceCollection == null)
            {
                throw new ArgumentNullException(nameof(serviceCollection));
            }

	        if (configuration == null)
	        {
		        throw new ArgumentNullException(nameof(configuration));
	        }
			serviceCollection.Configure<EventBusSetting>(configuration.GetSection(EventBusSetting.ConfigurationSectionName));
			serviceCollection.AddSingleton<IRabbitMQPersistentConnection, DefaultRabbitMQPersistentConnection>();
            serviceCollection.AddSingleton<IEventBus, EventBusRabbitMQ>();
            return serviceCollection;
        }
    }
}
