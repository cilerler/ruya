using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Configuration;
using Ruya.Services.DistributedLock.Extensions;
using Ruya.Services.DistributedLock.InMemory.Providers;

namespace Ruya.Services.DistributedLock.InMemory.Extensions;

/// <summary>
/// Extension methods for configuring in-memory distributed lock services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds in-memory lock manager (useful for testing).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureSettings">Optional action to configure lock manager settings.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method supports both programmatic configuration via the action parameter
    /// and configuration file-based setup. If no action is provided, settings will be
    /// automatically bound from the configuration section specified in DistributedLockSettings.ConfigurationSectionName.
    /// </remarks>
    public static IServiceCollection AddInMemoryDistributedLock(
        this IServiceCollection services,
        Action<DistributedLockSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register core services
        services.AddDistributedLockCore();

        // Register settings with configuration support
        services.AddOptions<DistributedLockSettings>()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                ArgumentNullException.ThrowIfNull(configuration);

                // Bind from configuration section
                var section = configuration.GetSection(DistributedLockSettings.ConfigurationSectionName);
                if (section.Exists())
                {
                    section.Bind(settings);
                }

                // Apply programmatic configuration if provided
                configureSettings?.Invoke(settings);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register InMemory provider
        services.TryAddSingleton<IDistributedLockProvider, InMemoryLockProvider>();

        return services;
    }
}
