using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Google;

namespace Ruya.Services.CloudStorage.Google
{
    public static class StartupExtensions
    {
        public static string RetrieveGoogleStorageCredentials(this IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (!configuration.GetSection(Setting.ConfigurationSectionName)
                              .Exists())
            {
                throw new ArgumentNullException(nameof(Setting.ConfigurationSectionName));
            }

            Dictionary<string, string> sectionItems = configuration.GetSection(Setting.ConfigurationSectionName)
                                                                   .GetChildren()
                                                                   .ToDictionary(item => item.Key
                                                                               , item => item.Value);
            string output = JsonConvert.SerializeObject(sectionItems);
            return output;
        }
    }
}

namespace Ruya
{
    public static partial class StartupExtensions
    {
        public static IServiceCollection AddGoogleStorageService(this IServiceCollection serviceCollection, IConfiguration configuration)
        {
            if (serviceCollection == null)
            {
                throw new ArgumentNullException(nameof(serviceCollection));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            serviceCollection.Configure<Setting>(options => options.Credential = configuration.RetrieveGoogleStorageCredentials());
            return serviceCollection.AddTransient<ICloudFileService, Client>();
        }

    }
}
