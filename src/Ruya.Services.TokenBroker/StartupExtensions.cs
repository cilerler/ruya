using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.TokenBroker.Contracts;

namespace Ruya.Services.TokenBroker;

public static class StartupExtensions
{
    /// <summary>
    /// Adds token issuance. The application must register an <see cref="IDistributedLock"/>
    /// backed by the same reliability tier as its distributed API-key cache.
    /// </summary>
    public static IServiceCollection AddTokenBroker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddTokenBrokerCore(services, configureSettings: null);
    }

    /// <summary>
    /// Adds token issuance with custom settings configuration.
    /// </summary>
    public static IServiceCollection AddTokenBroker(
        this IServiceCollection services,
        Action<TokenBrokerSettings> configureSettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureSettings);
        return AddTokenBrokerCore(services, configureSettings);
    }

    /// <summary>
    /// Adds both token issuance and public-key validation capabilities.
    /// </summary>
    public static IServiceCollection AddTokenBrokerWithValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTokenBroker();
        services.AddTokenValidation();
        return services;
    }

    private static IServiceCollection AddTokenBrokerCore(
        IServiceCollection services,
        Action<TokenBrokerSettings>? configureSettings)
    {
        var settingsBuilder = services.AddOptions<TokenBrokerSettings>()
            .BindConfiguration(TokenBrokerSettings.ConfigurationSectionName);
        if (configureSettings is not null)
        {
            settingsBuilder.Configure(configureSettings);
        }
        settingsBuilder.ValidateDataAnnotations().ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TokenBrokerSettings>, TokenBrokerDependencyValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITokenBroker, TokenBroker>();
        services.TryAddSingleton<IApiKeyValidator>(serviceProvider => new ApiKeyValidator(
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ApiKeyValidator>>(),
            serviceProvider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TokenBrokerSettings>>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            serviceProvider.GetRequiredService<IDistributedLock>()));

        services.ConfigureHttpJsonOptions(options =>
        {
            if (!options.SerializerOptions.TypeInfoResolverChain.Contains(TokenBrokerJsonSerializerContext.Default))
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, TokenBrokerJsonSerializerContext.Default);
            }
        });

        services.AddHealthChecks()
            .AddCheck<TokenBrokerHealthCheck>("token-service", tags: ["ready", "startup"]);

        return services;
    }
}
