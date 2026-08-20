using System;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using Polly;

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
        return AddTokenClientCore(services, configureSettings: null, configureResilience: null);
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
        return AddTokenClientCore(services, configureSettings: null, configureResilience);
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
        return AddTokenClientCore(services, configureSettings, configureResilience: null);
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

        return AddTokenClientCore(services, configureSettings, configureResilience);
    }

    private static IServiceCollection AddTokenClientCore(
        IServiceCollection services,
        Action<TokenClientSettings>? configureSettings,
        Action<HttpStandardResilienceOptions>? configureResilience)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TokenClientSettings>, TokenClientSettingsValidator>());

        services.AddMemoryCache();

        var settingsBuilder = services.AddOptions<TokenClientSettings>()
            .BindConfiguration(TokenClientSettings.ConfigurationSectionName);
        if (configureSettings is not null)
        {
            settingsBuilder.Configure(configureSettings);
        }
        settingsBuilder.ValidateDataAnnotations().ValidateOnStart();

        services.AddHttpClient<ITokenClient, TokenClient>()
            .AddStandardResilienceHandler(options =>
            {
                configureResilience?.Invoke(options);
                DisableRetriesForUnsafeMethods(options);
            });

        return services;
    }

    private static void DisableRetriesForUnsafeMethods(HttpStandardResilienceOptions options)
    {
        var defaultShouldHandle = options.Retry.ShouldHandle;
        options.Retry.ShouldHandle = arguments => IsRetrySafe(arguments.Context.GetRequestMessage()?.Method)
            ? defaultShouldHandle(arguments)
            : ValueTask.FromResult(false);
    }

    private static bool IsRetrySafe(HttpMethod? method) =>
        method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Options
        || method == HttpMethod.Trace;
}
