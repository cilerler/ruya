using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;

namespace Ruya.Services.MessageQueue.RabbitMQ;

/// <summary>
/// Extension methods for configuring RabbitMQ provider
/// </summary>
public static class RabbitMQExtensions
{
    /// <summary>
    /// Adds RabbitMQ provider to the message queue
    /// </summary>
    public static IMessageQueueBuilder AddRabbitMQ(
        this IMessageQueueBuilder builder,
        IConfiguration configuration)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        return builder.AddRabbitMQ(options =>
        {
            configuration.GetSection("MessageQueue:RabbitMQ").Bind(options);
        });
    }

    /// <summary>
    /// Adds RabbitMQ provider to the message queue
    /// </summary>
    public static IMessageQueueBuilder AddRabbitMQ(
        this IMessageQueueBuilder builder,
        Action<RabbitMQOptions> configureOptions)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        builder.Services.Configure(configureOptions);
        builder.Services.AddSingleton<IValidateOptions<RabbitMQOptions>, RabbitMQOptionsValidator>();
        builder.AddProvider<RabbitMQProvider>();

        return builder;
    }
}
