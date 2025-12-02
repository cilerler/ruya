using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Extensions;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Extension methods for configuring Redis provider
/// </summary>
public static class RedisExtensions
{
    /// <summary>
    /// Adds Redis provider to the message queue
    /// </summary>
    public static IMessageQueueBuilder AddRedis(
        this IMessageQueueBuilder builder,
        IConfiguration configuration)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        return builder.AddRedis(options =>
        {
            configuration.GetSection("MessageQueue:Redis").Bind(options);
        });
    }

    /// <summary>
    /// Adds Redis provider to the message queue
    /// </summary>
    public static IMessageQueueBuilder AddRedis(
        this IMessageQueueBuilder builder,
        Action<RedisOptions> configureOptions)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        builder.Services.Configure(configureOptions);
        builder.Services.AddSingleton<IValidateOptions<RedisOptions>, RedisOptionsValidator>();
        builder.AddProvider<RedisProvider>();

        return builder;
    }
}
