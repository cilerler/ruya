using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Azure;

namespace Ruya
{
	public static partial class StartupExtensions
	{
		public static IServiceCollection AddAzureStorageService(this IServiceCollection serviceCollection, IConfiguration configuration)
		{
			if (serviceCollection == null) throw new ArgumentNullException(nameof(serviceCollection));
			if (configuration == null) throw new ArgumentNullException(nameof(configuration));

			serviceCollection.Configure<Setting>(configuration.GetSection(Setting.ConfigurationSectionName));
			return serviceCollection.AddTransient<ICloudFileService, Client>();
		}
	}
}
