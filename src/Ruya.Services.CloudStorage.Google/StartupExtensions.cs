using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Google;

namespace Ruya;

public static partial class StartupExtensions
{
	public static IServiceCollection AddGoogleStorageService(this IServiceCollection serviceCollection)
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);

		serviceCollection.AddOptions<StorageServiceSettings>()
		.ValidateDataAnnotations()
		.ValidateOnStart()
		.Validate(settings => !string.IsNullOrWhiteSpace(settings.Credential), "Credential cannot be null.")
		.Configure<IConfiguration>((settings, configuration) =>
		{
			ArgumentNullException.ThrowIfNull(configuration);
			var section = configuration.GetSection(StorageServiceSettings.ConfigurationSectionName);
#pragma warning disable S3236 // Caller information arguments should not be provided explicitly
			ArgumentNullException.ThrowIfNull(section.Exists() ? string.Empty : null, StorageServiceSettings.ConfigurationSectionName);
#pragma warning restore S3236 // Caller information arguments should not be provided explicitly
			section.Bind(settings);

			var credential = configuration.GetSection(StorageServiceSettings.ConfigurationSectionName)[nameof(StorageServiceSettings.Credential)];
			if (string.IsNullOrWhiteSpace(credential))
			{
				var credentialSection = section.GetSection("Credential");
				if (credentialSection.Exists() && credentialSection.GetChildren().Any())
				{
					credential = JsonSerializer.Serialize(credentialSection.Get<Dictionary<string, object>>());
				}
			}
			ArgumentException.ThrowIfNullOrWhiteSpace(credential);
			settings.Credential = credential;
		});

		serviceCollection.AddKeyedTransient<ICloudFileService, Client>(StorageServiceSettings.ProviderName);
		serviceCollection.AddCloudStorageFactory();
		return serviceCollection;
	}
}
