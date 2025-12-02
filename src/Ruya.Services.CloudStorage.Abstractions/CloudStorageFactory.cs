using System;
using Microsoft.Extensions.DependencyInjection;

namespace Ruya.Services.CloudStorage.Abstractions;

public class CloudStorageFactory(IServiceProvider serviceProvider) : ICloudStorageFactory
{
    public ICloudFileService GetService(string providerKey)
    {
        if (serviceProvider is IKeyedServiceProvider keyedProvider)
        {
            var service = keyedProvider.GetKeyedService<ICloudFileService>(providerKey);
            if (service != null) return service;
        }

        throw new ArgumentException($"Cloud storage provider '{providerKey}' is not registered or IKeyedServiceProvider is not supported.", nameof(providerKey));
    }
}
