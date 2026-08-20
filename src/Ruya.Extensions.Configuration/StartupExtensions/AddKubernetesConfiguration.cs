using Microsoft.Extensions.Configuration;

namespace Ruya.Extensions.Configuration;

public static partial class StartupExtensions
{
    public static IConfigurationBuilder AddKubernetesConfiguration(this IConfigurationBuilder configuration)
    {
        System.ArgumentNullException.ThrowIfNull(configuration);

        // Configuration providers added later have higher precedence. Load the
        // non-secret ConfigMap first so a Secret can never be overridden by it.
        configuration.AddJsonFile("configuration/configmap/appsettings.ConfigMap.json", optional: true, reloadOnChange: true);
        configuration.AddJsonFile("configuration/secret/appsettings.Secrets.json", optional: true, reloadOnChange: true);
        return configuration;
    }
}
