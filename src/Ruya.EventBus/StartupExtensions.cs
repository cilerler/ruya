using System;
using Microsoft.Extensions.DependencyInjection;
using Ruya.EventBus;

// ReSharper disable once CheckNamespace
namespace Ruya
{
    public static partial class StartupExtensions
    {
        public static IServiceCollection AddEventBus(this IServiceCollection serviceCollection)
        {
            if (serviceCollection == null)
            {
                throw new ArgumentNullException(nameof(serviceCollection));
            }
            
            serviceCollection.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();
            return serviceCollection;
        }
    }
}
