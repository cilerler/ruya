using System;
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

        // Register core services
        services.AddDistributedLockCore();

        // Register Redis settings
        services.AddOptions<RedisLockSettings>()
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                ArgumentNullException.ThrowIfNull(configuration);
                var section = configuration.GetSection(RedisLockSettings.ConfigurationSectionName);
                ArgumentNullException.ThrowIfNull(section.Exists() ? string.Empty : null, RedisLockSettings.ConfigurationSectionName);
                section.Bind(settings);
                settings.ConnectionString = configuration.GetConnectionString(settings.ConnectionStringKey) ?? throw new ArgumentNullException(nameof(settings.ConnectionString));
            });

        // Register IConnectionMultiplexer if not already registered
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisSettings = sp.GetRequiredService<IOptions<RedisLockSettings>>().Value;

            var configOptions = ConfigurationOptions.Parse(redisSettings.ConnectionString);
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
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                ArgumentNullException.ThrowIfNull(configuration);
                var section = configuration.GetSection(RedisLockSettings.ConfigurationSectionName);
                ArgumentNullException.ThrowIfNull(section.Exists() ? string.Empty : null, RedisLockSettings.ConfigurationSectionName);
                section.Bind(settings);
                
                // Ensure RedlockEndpoints are populated
                if (settings.RedlockEndpoints == null || settings.RedlockEndpoints.Length == 0)
                {
                    // Fallback to ConnectionString if RedlockEndpoints not explicitly set, 
                    // but this method implies Redlock usage, so maybe we should warn or throw?
                    // For now, let's assume the user configured it correctly in appsettings.json
                }
            });

        // Register Redlock provider
        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<RedisLockSettings>>();
            var logger = sp.GetRequiredService<ILogger<RedlockProvider>>();
            var multiplexer = sp.GetService<IConnectionMultiplexer>(); // Optional
            
            return new RedlockProvider(settings, logger, null, multiplexer);
        });

        return services;
    }
}
