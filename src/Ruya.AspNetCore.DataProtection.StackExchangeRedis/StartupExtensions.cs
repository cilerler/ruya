using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ruya.AspNetCore.DataProtection.StackExchangeRedis.Contracts;

using StackExchange.Redis;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

/// <summary>
/// Extension methods for configuring data protection services.
/// </summary>
public static class StartupExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Adds data protection server services with Redis key persistence.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureSettings">Optional action to configure settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDataProtectionServer(
        this IServiceCollection services,
        Action<DataProtectionSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<DataProtectionSettings>()
            .BindConfiguration(DataProtectionSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configureSettings is not null)
        {
            optionsBuilder.Configure(configureSettings);
        }

        // Resolve ConnectionString from ConnectionStringKey
        optionsBuilder.Configure<IConfiguration>((settings, configuration) =>
        {
            settings.ConnectionString = configuration.GetConnectionString(settings.ConnectionStringKey)
                ?? throw new InvalidOperationException(
                    $"Connection string '{settings.ConnectionStringKey}' not found in configuration.");
        });

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DataProtectionSettings>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"Redis connection string is not configured. " +
                    $"Ensure ConnectionStrings:{options.ConnectionStringKey} is set in configuration.");
            }

            try
            {
                return ConnectionMultiplexer.Connect(options.ConnectionString);
            }
            catch (Exception ex)
            {
                var logger = sp.GetRequiredService<ILogger<DataProtectionService>>();
                logger.RedisConnectionFailed(ex);
                throw;
            }
        });

        ConfigureDataProtectionCommon(services);

        return services;
    }

    /// <summary>
    /// Adds data protection client services that fetch settings from a remote server.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="defaultPurpose">The default purpose string for data protection.</param>
    /// <param name="configureSettings">Optional action to configure settings after fetching.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Settings are fetched asynchronously from the remote endpoint using lazy initialization.
    /// Services that depend on data protection should handle the case where settings may not
    /// be ready yet. The health check will report unhealthy until initialization completes.
    /// </remarks>
    public static IServiceCollection AddDataProtectionClient(
        this IServiceCollection services,
        string defaultPurpose,
        Action<DataProtectionSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPurpose);

        services.AddOptions<DataProtectionClientSettings>()
            .BindConfiguration(DataProtectionClientSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                {
                    settings.ConnectionString = configuration.GetConnectionString(settings.ConnectionStringKey)
                        ?? throw new InvalidOperationException(
                            $"Connection string '{settings.ConnectionStringKey}' not found in configuration.");
                }
            });

        services.AddHttpClient(Constants.HttpClientName)
            .AddStandardResilienceHandler();

        // Register AsyncLazy for lazy async initialization of settings
        services.AddSingleton(sp =>
        {
            var clientSettings = sp.GetRequiredService<IOptions<DataProtectionClientSettings>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<DataProtectionService>>();

            return new AsyncLazy<DataProtectionSettings>(async () =>
            {
                var settings = await FetchSettingsAsync(clientSettings, httpClientFactory, logger).ConfigureAwait(false);
                settings.Purposes.Clear();
                settings.Purposes.Add(DataProtectionService.DefaultPurpose, defaultPurpose);
                configureSettings?.Invoke(settings);
                return settings;
            });
        });

        // Provide IOptions<DataProtectionSettings> that awaits the lazy value
        services.AddSingleton<IOptions<DataProtectionSettings>>(sp =>
        {
            var lazySettings = sp.GetRequiredService<AsyncLazy<DataProtectionSettings>>();
            // This will block on first access, but subsequent accesses return immediately
            var settings = lazySettings.Value.GetAwaiter().GetResult();
            return Options.Create(settings);
        });

        // Register AsyncLazy for lazy async initialization of Redis connection
        services.AddSingleton(sp =>
        {
            var lazySettings = sp.GetRequiredService<AsyncLazy<DataProtectionSettings>>();
            var logger = sp.GetRequiredService<ILogger<DataProtectionService>>();

            return new AsyncLazy<IConnectionMultiplexer>(async () =>
            {
                var settings = await lazySettings.Value.ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                {
                    throw new InvalidOperationException("Redis connection string is not configured.");
                }

                try
                {
                    return await ConnectionMultiplexer.ConnectAsync(settings.ConnectionString).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.RedisConnectionFailed(ex);
                    throw;
                }
            });
        });

        // Provide IConnectionMultiplexer that awaits the lazy value
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var lazyRedis = sp.GetRequiredService<AsyncLazy<IConnectionMultiplexer>>();
            return lazyRedis.Value.GetAwaiter().GetResult();
        });

        ConfigureDataProtectionClientCommon(services);

        return services;
    }

    private static void ConfigureDataProtectionClientCommon(IServiceCollection services)
    {
        services.AddSingleton<IConfigureOptions<DataProtectionOptions>>(sp =>
        {
            var lazySettings = sp.GetRequiredService<AsyncLazy<DataProtectionSettings>>();
            return new ConfigureNamedOptions<DataProtectionOptions>(
                Options.DefaultName,
                options =>
                {
                    if (lazySettings.IsValueCreated)
                    {
                        options.ApplicationDiscriminator = lazySettings.ValueOrDefault?.ApplicationName;
                    }
                });
        });

        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
        {
            var lazySettings = sp.GetRequiredService<AsyncLazy<DataProtectionSettings>>();
            var lazyRedis = sp.GetRequiredService<AsyncLazy<IConnectionMultiplexer>>();
            return new ConfigureNamedOptions<KeyManagementOptions>(
                Options.DefaultName,
                options =>
                {
                    if (lazySettings.IsValueCreated && lazyRedis.IsValueCreated)
                    {
                        var settings = lazySettings.ValueOrDefault!;
                        var redis = lazyRedis.ValueOrDefault!;
                        options.NewKeyLifetime = TimeSpan.FromDays(settings.DefaultKeyLifetime);
                        options.XmlRepository = new Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository(
                            () => redis.GetDatabase(),
                            settings.CacheKey);
                    }
                });
        });

        services.AddDataProtection();

        services.AddSingleton<IDataProtection, DataProtectionService>();

        services.AddHealthChecks()
            .AddCheck<DataProtectionHealthCheck>("dataprotection-redis");
    }

    private static void ConfigureDataProtectionCommon(IServiceCollection services)
    {
        services.AddSingleton<IConfigureOptions<DataProtectionOptions>>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<DataProtectionSettings>>().Value;
            return new ConfigureNamedOptions<DataProtectionOptions>(
                Options.DefaultName,
                options => options.ApplicationDiscriminator = settings.ApplicationName);
        });

        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<DataProtectionSettings>>().Value;
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new ConfigureNamedOptions<KeyManagementOptions>(
                Options.DefaultName,
                options =>
                {
                    options.NewKeyLifetime = TimeSpan.FromDays(settings.DefaultKeyLifetime);
                    options.XmlRepository = new Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository(
                        () => redis.GetDatabase(),
                        settings.CacheKey);
                });
        });

        services.AddDataProtection();

        services.AddSingleton<IDataProtection, DataProtectionService>();

        services.AddHealthChecks()
            .AddCheck<DataProtectionHealthCheck>("dataprotection-redis");
    }

    private static async Task<DataProtectionSettings> FetchSettingsAsync(
        DataProtectionClientSettings clientSettings,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(clientSettings.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Data protection client connection string is not configured. " +
                $"Ensure ConnectionStrings:{clientSettings.ConnectionStringKey} is set in configuration.");
        }

        var client = httpClientFactory.CreateClient(Constants.HttpClientName);
        var baseUri = new Uri(clientSettings.ConnectionString);
        var fullUri = new Uri(baseUri, clientSettings.Endpoint);

        try
        {
            var response = await client.GetAsync(fullUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<DataProtectionSettings>(content, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize data protection settings.");

            logger.SettingsFetchSucceeded(fullUri.ToString());
            return settings;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.SettingsFetchFailed(fullUri.ToString(), ex);
            throw new InvalidOperationException($"Failed to fetch data protection settings from {fullUri}", ex);
        }
    }
}
