using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;

namespace Ruya.Services.MessageQueue.RabbitMq;

/// <summary>
/// Extension methods for configuring RabbitMQ provider
/// </summary>
public static class RabbitMQExtensions
{
    /// <summary>
    /// Adds the RabbitMQ provider using an explicitly supplied configuration root.
    /// </summary>
    [Obsolete("Use AddRabbitMQ() and configure the 'MessageQueue:RabbitMQ' section. The IConfiguration overload will be removed in version 9.0.")]
    public static IMessageQueueBuilder AddRabbitMQ(
        this IMessageQueueBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        return builder.AddRabbitMQ(options =>
            configuration.GetSection(RabbitMQOptions.ConfigurationSectionName).Bind(options));
    }

    /// <summary>
    /// Adds RabbitMQ provider to the message queue and binds options from
    /// <see cref="RabbitMQOptions.ConfigurationSectionName"/>.
    /// </summary>
    public static IMessageQueueBuilder AddRabbitMQ(this IMessageQueueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<RabbitMQOptions>()
            .BindConfiguration(RabbitMQOptions.ConfigurationSectionName)
            .ValidateOnStart();

        return AddRabbitMQProvider(builder);
    }

    /// <summary>
    /// Adds RabbitMQ provider to the message queue
    /// </summary>
    public static IMessageQueueBuilder AddRabbitMQ(
        this IMessageQueueBuilder builder,
        Action<RabbitMQOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        builder.Services.AddOptions<RabbitMQOptions>()
            .Configure(configureOptions)
            .ValidateOnStart();

        return AddRabbitMQProvider(builder);
    }

    private static IMessageQueueBuilder AddRabbitMQProvider(IMessageQueueBuilder builder)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RabbitMQOptions>, RabbitMQOptionsValidator>());
        builder.AddProvider<RabbitMQProvider>();

        return builder;
    }
}
