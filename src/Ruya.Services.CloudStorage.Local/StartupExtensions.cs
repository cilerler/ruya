using System;

using Microsoft.Extensions.DependencyInjection;

using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Local;

namespace Ruya;

public static partial class StartupExtensions
{
    public static IServiceCollection AddLocalStorageService(this IServiceCollection serviceCollection)
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);

		serviceCollection.AddOptions<StorageServiceSettings>()
		.BindConfiguration(StorageServiceSettings.ConfigurationSectionName)
		.ValidateDataAnnotations()
		.ValidateOnStart()
		.Validate(settings => !string.IsNullOrWhiteSpace(settings.Path), "Path cannot be null.");

		serviceCollection.AddKeyedSingleton<ICloudFileService, Client>(StorageServiceSettings.ProviderName);
		serviceCollection.AddCloudStorageFactory();
		return serviceCollection;
	}
}
