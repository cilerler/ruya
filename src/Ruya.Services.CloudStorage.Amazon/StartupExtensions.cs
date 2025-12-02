using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Amazon;

namespace Ruya;

public static partial class StartupExtensions
{
    public static IServiceCollection AddAmazonStorageService(this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        if (serviceCollection == null) throw new ArgumentNullException(nameof(serviceCollection));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        serviceCollection.Configure<Setting>(configuration.GetSection(Setting.ConfigurationSectionName));
        serviceCollection.AddKeyedTransient<ICloudFileService, Client>(Setting.ProviderName);
        serviceCollection.AddCloudStorageFactory();
        return serviceCollection;
    }
}
