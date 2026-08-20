using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Extensions;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Extension methods for configuring Redis provider
/// </summary>
public static class RedisExtensions
{
    /// <summary>
    /// Adds Redis provider options bound from <c>MessageQueue:Redis</c>.
    /// </summary>
    public static IMessageQueueBuilder AddRedis(this IMessageQueueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<RedisOptions>()
            .BindConfiguration(RedisOptions.ConfigurationSectionName)
            .ValidateOnStart();
        RegisterProvider(builder);
        return builder;
    }

    /// <summary>
    /// Adds Redis provider to the message queue
    /// </summary>
    [Obsolete("Use AddRedis() to bind MessageQueue:Redis or AddRedis(Action<RedisOptions>) for typed configuration. This overload will be removed in version 9.0.")]
    public static IMessageQueueBuilder AddRedis(
        this IMessageQueueBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        return builder.AddRedis(options =>
        {
            configuration.GetSection(RedisOptions.ConfigurationSectionName).Bind(options);
            ResolveConnectionString(options, configuration);
        });
    }

    /// <summary>
    /// Adds Redis provider to the message queue
    /// </summary>
    public static IMessageQueueBuilder AddRedis(
        this IMessageQueueBuilder builder,
        Action<RedisOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        builder.Services
            .AddOptions<RedisOptions>()
            .Configure(configureOptions)
            .ValidateOnStart();
        RegisterProvider(builder);

        return builder;
    }

    private static void RegisterProvider(IMessageQueueBuilder builder)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RedisOptions>, RedisOptionsValidator>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RedisOptions>, RedisConnectionStringCatalogValidator>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<RedisOptions>, RedisConnectionStringResolver>());
        builder.AddProvider<RedisProvider>();
    }

    internal static void ResolveConnectionString(RedisOptions options, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(options.RedisConnectionStringKey) ||
            options.ConnectionStringResolvedFromCatalog)
        {
            return;
        }

        var resolvedConnectionString = configuration.GetConnectionString(options.RedisConnectionStringKey);
        options.ConnectionString = resolvedConnectionString ?? string.Empty;
        options.ConnectionStringResolvedFromCatalog = !string.IsNullOrWhiteSpace(resolvedConnectionString);
    }
}

internal sealed class RedisConnectionStringResolver : IConfigureOptions<RedisOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public RedisConnectionStringResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void Configure(RedisOptions options)
    {
        var configuration = _serviceProvider.GetService<IConfiguration>();
        if (configuration is not null)
        {
            RedisExtensions.ResolveConnectionString(options, configuration);
        }
    }
}
