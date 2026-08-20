using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Extensions;
using Ruya.Services.DistributedLock.Redis.Configuration;
using Ruya.Services.DistributedLock.Redis.Providers;
using StackExchange.Redis;

namespace Ruya.Services.DistributedLock.Redis.Extensions;

/// <summary>
/// Extension methods for configuring Redis-based distributed lock services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Redis-based lock manager.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisDistributedLock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        bool useExistingMultiplexer = services.Any(
            descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer) &&
                !descriptor.IsKeyedService);

        // Register core services
        services.AddDistributedLockCore();

        // Register Redis settings
        services.AddOptions<RedisLockSettings>()
            .BindConfiguration(RedisLockSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .Validate<IConfiguration>(
                (settings, configuration) =>
                    useExistingMultiplexer ||
                    !string.IsNullOrWhiteSpace(settings.ConnectionStringKey) &&
                    !string.IsNullOrWhiteSpace(configuration.GetConnectionString(settings.ConnectionStringKey)),
                "A caller-supplied IConnectionMultiplexer or a configured Redis connection-string catalog entry is required.")
            .ValidateOnStart();

        // Register IConnectionMultiplexer if not already registered
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisSettings = sp.GetRequiredService<IOptions<RedisLockSettings>>().Value;
            var configuration = sp.GetRequiredService<IConfiguration>();
            string connectionString = configuration.GetConnectionString(redisSettings.ConnectionStringKey)
                ?? throw new InvalidOperationException(
                    $"Connection string catalog entry '{redisSettings.ConnectionStringKey}' is not configured.");

            var configOptions = ConfigurationOptions.Parse(connectionString);
            configOptions.SyncTimeout = redisSettings.SyncTimeoutMs;
            configOptions.AbortOnConnectFail = redisSettings.AbortOnConnectFail;

            return ConnectionMultiplexer.Connect(configOptions);
        });

        // Register Redis provider
        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new RedisLockProvider(multiplexer, loggerFactory.CreateLogger<RedisLockProvider>());
        });

        return services;
    }
    /// <summary>
    /// Adds Redlock (multi-master) distributed lock manager.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedlockDistributedLock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register core services
        services.AddDistributedLockCore();

        // Register Redis settings
        services.AddOptions<RedisLockSettings>()
            .BindConfiguration(RedisLockSettings.ConfigurationSectionName)
            .Validate(
                settings => settings.SyncTimeoutMs is >= 1000 and <= 30000,
                "SyncTimeoutMs must be between 1000 and 30000.")
            .Validate(
                settings => settings.RedlockConnectionStringKeys is not { Length: > 0 } keys ||
                    keys.All(key => !string.IsNullOrWhiteSpace(key)) &&
                    keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == keys.Length,
                "Redlock connection-string catalog keys must be nonblank and unique.")
            .Validate<IConfiguration, IServiceProviderIsService>(
                (settings, configuration, availableServices) =>
                {
                    string[] endpoints = ResolveRedlockEndpoints(settings, configuration);
                    return endpoints.Length == 0
                        ? availableServices.IsService(typeof(IConnectionMultiplexer))
                        : RedlockEndpointSetValidator.IsValid(endpoints);
                },
                "Redlock requires an odd number of at least three independent Redis endpoints, or a caller-supplied IConnectionMultiplexer.")
            .ValidateOnStart();

        // Register Redlock provider
        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<RedisLockSettings>>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            string[] endpoints = ResolveRedlockEndpoints(settings.Value, configuration);
            var logger = sp.GetRequiredService<ILogger<RedlockProvider>>();
            var multiplexer = sp.GetService<IConnectionMultiplexer>(); // Optional
            
            return new RedlockProvider(settings, endpoints, logger, null, multiplexer);
        });

        return services;
    }

    private static string[] ResolveRedlockEndpoints(
        RedisLockSettings settings,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(configuration);

        if (settings.RedlockConnectionStringKeys is { Length: > 0 } keys)
        {
            return keys
                .Select(key => string.IsNullOrWhiteSpace(key)
                    ? string.Empty
                    : configuration.GetConnectionString(key) ?? string.Empty)
                .ToArray();
        }

        return settings.RedlockEndpoints ?? [];
    }

}
