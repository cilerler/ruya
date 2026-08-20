using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Google;

namespace Ruya;

public static partial class StartupExtensions
{
	public static IServiceCollection AddGoogleStorageService(this IServiceCollection serviceCollection)
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);

		serviceCollection.AddOptions<StorageServiceSettings>()
		.BindConfiguration(StorageServiceSettings.ConfigurationSectionName)
		.ValidateDataAnnotations()
		.ValidateOnStart()
		.Validate(settings => !string.IsNullOrWhiteSpace(settings.Credential), "Credential cannot be null.")
		.PostConfigure<IConfiguration>((settings, configuration) =>
		{
			ArgumentNullException.ThrowIfNull(configuration);
			var section = configuration.GetSection(StorageServiceSettings.ConfigurationSectionName);
			if (!section.Exists()) return;

			var credential = configuration.GetSection(StorageServiceSettings.ConfigurationSectionName)[nameof(StorageServiceSettings.Credential)];
			if (string.IsNullOrWhiteSpace(credential))
			{
				var credentialSection = section.GetSection(nameof(StorageServiceSettings.Credential));
				if (credentialSection.Exists() && credentialSection.GetChildren().Any())
				{
					credential = JsonSerializer.Serialize(credentialSection.Get<Dictionary<string, object>>());
				}
			}
			ArgumentException.ThrowIfNullOrWhiteSpace(credential);
			settings.Credential = credential;
		});

		serviceCollection.AddKeyedSingleton<ICloudFileService, Client>(StorageServiceSettings.ProviderName);
		serviceCollection.AddCloudStorageFactory();
		return serviceCollection;
	}
}
