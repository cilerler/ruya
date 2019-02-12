using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Bus.Abstractions;
using Ruya.Bus.RabbitMQ;

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
	        // ReSharper disable once AccessToStaticMemberViaDerivedType
	        serviceCollection.Configure<BusSetting>(configuration.GetSection(BusSetting.ConfigurationSectionName));
			serviceCollection.AddSingleton<IRabbitMqPersistentConnection, DefaultRabbitMqPersistentConnection>();
            serviceCollection.AddTransient<IEventBus, EventBusRabbitMq>();
            return serviceCollection;
        }
    }
}
