using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Ruya.Services.TokenBroker.Client;

public static class TokenClientStartupExtensions
{
    /// <summary>
    /// Adds the Token Client for services that need to request tokens.
    /// Includes built-in resilience with retry, circuit breaker, and timeout.
    /// </summary>
    public static IServiceCollection AddTokenClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMemoryCache();

        services.AddOptions<TokenClientSettings>()
            .BindConfiguration(TokenClientSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ITokenClient, TokenClient>()
            .AddStandardResilienceHandler();

        return services;
    }

    /// <summary>
    /// Adds the Token Client with custom resilience options.
    /// </summary>
    public static IServiceCollection AddTokenClient(
        this IServiceCollection services,
        Action<HttpStandardResilienceOptions> configureResilience)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureResilience);

        services.AddMemoryCache();

        services.AddOptions<TokenClientSettings>()
            .BindConfiguration(TokenClientSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ITokenClient, TokenClient>()
            .AddStandardResilienceHandler(configureResilience);

        return services;
    }

    /// <summary>
    /// Adds the Token Client with custom settings configuration.
    /// </summary>
    public static IServiceCollection AddTokenClient(
        this IServiceCollection services,
        Action<TokenClientSettings> configureSettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureSettings);

        services.AddMemoryCache();

        services.AddOptions<TokenClientSettings>()
            .BindConfiguration(TokenClientSettings.ConfigurationSectionName)
            .Configure(configureSettings)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ITokenClient, TokenClient>()
            .AddStandardResilienceHandler();

        return services;
    }

    /// <summary>
    /// Adds the Token Client with custom settings and resilience configuration.
    /// </summary>
    public static IServiceCollection AddTokenClient(
        this IServiceCollection services,
        Action<TokenClientSettings> configureSettings,
        Action<HttpStandardResilienceOptions> configureResilience)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureSettings);
        ArgumentNullException.ThrowIfNull(configureResilience);

        services.AddMemoryCache();

        services.AddOptions<TokenClientSettings>()
            .BindConfiguration(TokenClientSettings.ConfigurationSectionName)
            .Configure(configureSettings)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ITokenClient, TokenClient>()
            .AddStandardResilienceHandler(configureResilience);

        return services;
    }
}
