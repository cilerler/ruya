using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ruya.Services.CloudStorage.Abstractions;

public static class StartupExtensions
{
    public static IServiceCollection AddCloudStorageFactory(this IServiceCollection services)
    {
        services.TryAddSingleton<ICloudStorageFactory, CloudStorageFactory>();
        return services;
    }
}
