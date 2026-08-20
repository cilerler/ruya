using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <summary>
    /// Adds data protection services with Redis key persistence.
    /// </summary>
    public static IServiceCollection AddDataProtectionServer(
        this IServiceCollection services,
        Action<DataProtectionSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<DataProtectionSettings>()
            .BindConfiguration(DataProtectionSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .Validate<IConfiguration>(
                HasValidRedisConnectionString,
                "The referenced Redis connection string is missing or invalid.")
            .Validate(
                settings => settings.Purposes.All(purpose =>
                    !string.IsNullOrWhiteSpace(purpose.Key) &&
                    !string.IsNullOrWhiteSpace(purpose.Value)),
                "DataProtectionSettings purpose names and values must be nonblank.")
            .ValidateOnStart();

        if (configureSettings is not null)
        {
            optionsBuilder.Configure(configureSettings);
        }

        optionsBuilder.Configure<IConfiguration>((settings, configuration) =>
        {
            settings.ConnectionString = configuration.GetConnectionString(settings.ConnectionStringKey)!;
        });

        services.TryAddSingleton<IRedisConnectionFactory, RedisConnectionFactory>();
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<DataProtectionSettings>>().Value;

            try
            {
                return sp.GetRequiredService<IRedisConnectionFactory>().Connect(settings.ConnectionString);
            }
            catch (Exception ex)
            {
                sp.GetRequiredService<ILogger<DataProtectionService>>()
                    .RedisConnectionFailed(ex.GetType().Name);
                throw new InvalidOperationException(
                    "Failed to initialize the Redis connection for data protection.");
            }
        });

        ConfigureDataProtectionCommon(services);
        return services;
    }

    /// <summary>
    /// Adds data protection client services that fetch settings from a remote server.
    /// </summary>
    /// <remarks>
    /// The remote settings are fetched once, and one singleton Redis connection is registered for
    /// Data Protection and for Redis-backed components registered afterward that borrow an existing
    /// <see cref="IConnectionMultiplexer"/>.
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
            .Validate<IConfiguration>(
                HasValidRemoteEndpoint,
                "The referenced data protection settings endpoint is missing or invalid.")
            .ValidateOnStart()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetConnectionString(settings.ConnectionStringKey)!;
            });

        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .AddStandardResilienceHandler();
        services.TryAddSingleton<IRedisConnectionFactory, RedisConnectionFactory>();

        services.AddSingleton(sp =>
        {
            var clientSettings = sp.GetRequiredService<IOptions<DataProtectionClientSettings>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<DataProtectionService>>();

            return new AsyncLazy<DataProtectionSettings>(async () =>
            {
                var settings = await FetchSettingsAsync(
                    clientSettings,
                    httpClientFactory,
                    logger).ConfigureAwait(false);
                settings.Purposes.Clear();
                settings.Purposes.Add(DataProtectionService.DefaultPurpose, defaultPurpose);
                configureSettings?.Invoke(settings);
                ValidateFetchedSettings(settings);
                return settings;
            });
        });

        services.AddSingleton<IOptions<DataProtectionSettings>>(sp =>
        {
            var settings = sp.GetRequiredService<AsyncLazy<DataProtectionSettings>>()
                .Value.GetAwaiter().GetResult();
            return Options.Create(settings);
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<AsyncLazy<DataProtectionSettings>>();
            var connectionFactory = sp.GetRequiredService<IRedisConnectionFactory>();
            var logger = sp.GetRequiredService<ILogger<DataProtectionService>>();

            return new RedisConnectionLifetime(async () =>
            {
                var resolvedSettings = await settings.Value.ConfigureAwait(false);

                try
                {
                    return await connectionFactory.ConnectAsync(
                        resolvedSettings.ConnectionString).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.RedisConnectionFailed(ex.GetType().Name);
                    throw new InvalidOperationException(
                        "Failed to initialize the Redis connection for data protection.");
                }
            });
        });

        services.AddSingleton(sp =>
            sp.GetRequiredService<RedisConnectionLifetime>().Connection);

        services.AddSingleton<IConnectionMultiplexer>(sp =>
            sp.GetRequiredService<RedisConnectionLifetime>()
                .GetContainerOwnedConnection());

        ConfigureDataProtectionCommon(services);
        return services;
    }

    /// <summary>
    /// Asynchronously initializes remote data protection settings and the shared Redis connection.
    /// </summary>
    /// <remarks>
    /// External clients can await this method during application startup so later synchronous Data
    /// Protection operations do not perform their first remote initialization on the calling thread.
    /// </remarks>
    public static async Task InitializeDataProtectionClientAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        cancellationToken.ThrowIfCancellationRequested();

        var settings = serviceProvider.GetService<AsyncLazy<DataProtectionSettings>>()
            ?? throw new InvalidOperationException(
                "Remote data protection client services are not registered. Call AddDataProtectionClient first.");
        await settings.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

        var connection = serviceProvider.GetService<AsyncLazy<IConnectionMultiplexer>>();
        if (connection is not null)
        {
            await connection.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            _ = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        }
    }

    private static void ConfigureDataProtectionCommon(IServiceCollection services)
    {
        services.AddDataProtection();

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

        services.AddSingleton<IDataProtection, DataProtectionService>();
        services.AddHealthChecks().AddCheck<DataProtectionHealthCheck>("dataprotection-redis");
    }

    private static async Task<DataProtectionSettings> FetchSettingsAsync(
        DataProtectionClientSettings clientSettings,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        var fullUri = ResolveRemoteEndpoint(clientSettings);
        var safeEndpoint = fullUri.GetComponents(
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped);

        try
        {
            var client = httpClientFactory.CreateClient(DataProtectionClientSettings.HttpClientName);
            using var response = await client.GetAsync(fullUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var settings = await JsonSerializer.DeserializeAsync(
                content,
                DataProtectionJsonSerializerContext.Default.DataProtectionSettings).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The remote data protection settings response was empty.");

            ValidateFetchedSettings(settings);
            logger.SettingsFetchSucceeded(safeEndpoint);
            return settings;
        }
        catch (Exception ex)
        {
            logger.SettingsFetchFailed(safeEndpoint, ex.GetType().Name);
            throw new InvalidOperationException(
                $"Failed to fetch data protection settings from {safeEndpoint}.");
        }
    }

    private static bool HasValidRedisConnectionString(
        DataProtectionSettings settings,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionStringKey))
        {
            return false;
        }

        var connectionString = configuration.GetConnectionString(settings.ConnectionStringKey);
        return IsValidRedisConnectionString(connectionString);
    }

    private static bool HasValidRemoteEndpoint(
        DataProtectionClientSettings settings,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionStringKey) ||
            string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return false;
        }

        var serviceAddress = configuration.GetConnectionString(settings.ConnectionStringKey);
        return TryResolveRemoteEndpoint(serviceAddress, settings.Endpoint, out _);
    }

    private static Uri ResolveRemoteEndpoint(DataProtectionClientSettings settings) =>
        TryResolveRemoteEndpoint(settings.ConnectionString, settings.Endpoint, out var endpoint)
            ? endpoint
            : throw new InvalidOperationException("The remote data protection settings endpoint is invalid.");

    private static bool TryResolveRemoteEndpoint(
        string? serviceAddress,
        string? endpoint,
        out Uri resolvedEndpoint)
    {
        resolvedEndpoint = null!;
        if (!Uri.TryCreate(serviceAddress, UriKind.Absolute, out var baseUri) ||
            string.IsNullOrWhiteSpace(endpoint) ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !Uri.TryCreate(baseUri, endpoint, out var fullUri) ||
            !string.IsNullOrEmpty(fullUri.UserInfo) ||
            !IsSameOrigin(baseUri, fullUri) ||
            !IsSecureRemoteEndpoint(fullUri))
        {
            return false;
        }

        resolvedEndpoint = fullUri;
        return true;
    }

    private static bool IsSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static bool IsSecureRemoteEndpoint(Uri endpoint) =>
        string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
        endpoint.IsLoopback;

    private static void ValidateFetchedSettings(DataProtectionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApplicationName) ||
            string.IsNullOrWhiteSpace(settings.ConnectionStringKey) ||
            string.IsNullOrWhiteSpace(settings.CacheKey) ||
            settings.DefaultKeyLifetime is < 1 or > 365 ||
            settings.Purposes.Any(purpose =>
                string.IsNullOrWhiteSpace(purpose.Key) ||
                string.IsNullOrWhiteSpace(purpose.Value)) ||
            !IsValidRedisConnectionString(settings.ConnectionString))
        {
            throw new InvalidOperationException("The remote data protection settings are invalid.");
        }
    }

    private static bool IsValidRedisConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            _ = ConfigurationOptions.Parse(connectionString);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }
}
