using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Azure;

namespace Ruya
{
	public static partial class StartupExtensions
	{
		public static IServiceCollection AddAzureStorageService(this IServiceCollection serviceCollection)
		{
			ArgumentNullException.ThrowIfNull(serviceCollection);

			OptionsBuilder<Setting> options = serviceCollection.AddOptions<Setting>()
				.BindConfiguration(Setting.ConfigurationSectionName);
			return AddAzureStorageServiceCore(serviceCollection, options, explicitConfiguration: null);
		}

		public static IServiceCollection AddAzureStorageService(this IServiceCollection serviceCollection, IConfiguration configuration)
		{
			ArgumentNullException.ThrowIfNull(serviceCollection);
			ArgumentNullException.ThrowIfNull(configuration);

			OptionsBuilder<Setting> options = serviceCollection.AddOptions<Setting>()
				.Bind(configuration.GetSection(Setting.ConfigurationSectionName));
			return AddAzureStorageServiceCore(serviceCollection, options, configuration);
		}

		private static IServiceCollection AddAzureStorageServiceCore(
			IServiceCollection serviceCollection,
			OptionsBuilder<Setting> options,
			IConfiguration? explicitConfiguration)
		{
			options
				.Validate(
					settings => !string.IsNullOrWhiteSpace(settings.ConnectionStringKey),
					"ConnectionStringKey is required.");

			if (explicitConfiguration is null)
			{
				options.Validate<IConfiguration>(
					(settings, configuration) =>
						!string.IsNullOrWhiteSpace(configuration.GetConnectionString(settings.ConnectionStringKey)),
					"The configured Azure storage connection-string catalog entry is required.");
			}
			else
			{
				options.Validate(
					settings => !string.IsNullOrWhiteSpace(explicitConfiguration.GetConnectionString(settings.ConnectionStringKey)),
					"The configured Azure storage connection-string catalog entry is required.");
			}

			options.ValidateOnStart();

			if (explicitConfiguration is null)
			{
				serviceCollection.AddKeyedSingleton<ICloudFileService, Client>(Setting.ProviderName);
			}
			else
			{
				serviceCollection.AddKeyedSingleton<ICloudFileService>(
					Setting.ProviderName,
					(serviceProvider, _) => new Client(
						explicitConfiguration,
						serviceProvider.GetRequiredService<ILogger<Client>>(),
						serviceProvider.GetRequiredService<IOptions<Setting>>()));
			}

            serviceCollection.AddCloudStorageFactory();
            return serviceCollection;
		}
	}
}
