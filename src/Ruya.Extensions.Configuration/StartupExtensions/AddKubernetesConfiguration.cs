using Microsoft.Extensions.Configuration;

namespace Ruya.Extensions.Configuration;

public static partial class StartupExtensions
{
    public static IConfigurationBuilder AddKubernetesConfiguration(this IConfigurationBuilder configuration)
    {
        configuration.AddJsonFile("configuration/secret/appsettings.Secrets.json", optional: true, reloadOnChange: true);
        configuration.AddJsonFile("configuration/configmap/appsettings.ConfigMap.json", optional: true, reloadOnChange: true);
        return configuration;
    }
}
