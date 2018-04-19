using System;
using Microsoft.Extensions.DependencyInjection;
using Ruya.EventBus.Abstractions;
using Ruya.EventBus.RabbitMQ;

// ReSharper disable once CheckNamespace
namespace Ruya
{
    public static partial class StartupExtensions
    {
        public static IServiceCollection AddEventBusRabbitMq(this IServiceCollection serviceCollection)
        {
            if (serviceCollection == null)
            {
                throw new ArgumentNullException(nameof(serviceCollection));
            }
            serviceCollection.AddSingleton<IRabbitMQPersistentConnection>();
            serviceCollection.AddSingleton<IEventBus, EventBusRabbitMQ>();
            return serviceCollection;
        }
    }
}
