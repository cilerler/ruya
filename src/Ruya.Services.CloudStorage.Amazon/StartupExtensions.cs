using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Amazon;

namespace Ruya;

public static partial class StartupExtensions
{
    public static IServiceCollection AddAmazonStorageService(this IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        OptionsBuilder<Setting> options = serviceCollection.AddOptions<Setting>()
            .BindConfiguration(Setting.ConfigurationSectionName);
        return AddAmazonStorageServiceCore(serviceCollection, options);
    }

    public static IServiceCollection AddAmazonStorageService(this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(configuration);

        OptionsBuilder<Setting> options = serviceCollection.AddOptions<Setting>()
            .Bind(configuration.GetSection(Setting.ConfigurationSectionName));
        return AddAmazonStorageServiceCore(serviceCollection, options);
    }

    private static IServiceCollection AddAmazonStorageServiceCore(
        IServiceCollection serviceCollection,
        OptionsBuilder<Setting> options)
    {
        options
            .Validate(
                settings => string.IsNullOrWhiteSpace(settings.AccessKey) == string.IsNullOrWhiteSpace(settings.SecretKey),
                "AccessKey and SecretKey must either both be configured or both be omitted so the AWS default credential chain is used.")
            .ValidateOnStart();

        serviceCollection.AddKeyedSingleton<ICloudFileService, Client>(Setting.ProviderName);
        serviceCollection.AddCloudStorageFactory();
        return serviceCollection;
    }
}
