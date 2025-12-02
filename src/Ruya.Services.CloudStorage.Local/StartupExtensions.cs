using System;

using Microsoft.Extensions.Configuration;
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
		.ValidateDataAnnotations()
		.ValidateOnStart()
		.Validate(settings => !string.IsNullOrWhiteSpace(settings.Path), "Path cannot be null.")
		.Configure<IConfiguration>((settings, configuration) =>
		{
			ArgumentNullException.ThrowIfNull(configuration);
			var section = configuration.GetSection(StorageServiceSettings.ConfigurationSectionName);
#pragma warning disable S3236 // Caller information arguments should not be provided explicitly
			ArgumentNullException.ThrowIfNull(section.Exists() ? string.Empty : null, StorageServiceSettings.ConfigurationSectionName);
#pragma warning restore S3236 // Caller information arguments should not be provided explicitly
			section.Bind(settings);
		});

		serviceCollection.AddKeyedTransient<ICloudFileService, Client>(StorageServiceSettings.ProviderName);
		serviceCollection.AddCloudStorageFactory();
		return serviceCollection;
	}
}
