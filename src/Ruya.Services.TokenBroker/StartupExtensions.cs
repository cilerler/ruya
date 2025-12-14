using System;

using Microsoft.Extensions.DependencyInjection;

using Ruya.Services.TokenBroker.Contracts;

namespace Ruya.Services.TokenBroker;

public static class StartupExtensions
{
    /// <summary>
    /// Adds the Token Service for issuing JWTs.
    /// Use this in the service that creates tokens (the central Token Service).
    /// Requires Redis for API key storage.
    /// </summary>
    public static IServiceCollection AddTokenBroker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TokenBrokerSettings>()
            .BindConfiguration(TokenBrokerSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenBroker, TokenBroker>();
        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();

        services.AddHealthChecks()
            .AddCheck<TokenBrokerHealthCheck>("token-service");

        return services;
    }

    /// <summary>
    /// Adds the Token Service with custom settings configuration.
    /// </summary>
    public static IServiceCollection AddTokenBroker(
        this IServiceCollection services,
        Action<TokenBrokerSettings> configureSettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureSettings);

        services.AddOptions<TokenBrokerSettings>()
            .BindConfiguration(TokenBrokerSettings.ConfigurationSectionName)
            .Configure(configureSettings)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenBroker, TokenBroker>();
        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();

        services.AddHealthChecks()
            .AddCheck<TokenBrokerHealthCheck>("token-service");

        return services;
    }

    /// <summary>
    /// Adds both token issuance and validation capabilities.
    /// Use this when the Token Service itself needs to validate incoming tokens (e.g., for admin endpoints).
    /// </summary>
    public static IServiceCollection AddTokenBrokerWithValidation(this IServiceCollection services)
    {
        services.AddTokenBroker();
        services.AddTokenValidation();
        return services;
    }
}
