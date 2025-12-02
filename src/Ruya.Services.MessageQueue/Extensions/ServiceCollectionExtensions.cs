using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Factory;
using Ruya.Services.MessageQueue.Serialization;

namespace Ruya.Services.MessageQueue.Extensions;

/// <summary>
/// Extension methods for configuring Ruya.Services.MessageQueue services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Ruya.Services.MessageQueue services to the service collection
    /// </summary>
    public static IMessageQueueBuilder AddMessageQueue(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        return services.AddMessageQueue(options =>
        {
            configuration.GetSection("MessageQueue").Bind(options);
        });
    }

    /// <summary>
    /// Adds Ruya.Services.MessageQueue services to the service collection
    /// </summary>
    public static IMessageQueueBuilder AddMessageQueue(
        this IServiceCollection services,
        Action<MessageQueueOptions> configureOptions)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

        // Configure options
        services.Configure(configureOptions);
        services.AddSingleton<IValidateOptions<MessageQueueOptions>, MessageQueueOptionsValidator>();

        // Register core services
        services.TryAddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.TryAddSingleton<IMessageQueueFactory, MessageQueueFactory>();

        return new MessageQueueBuilder(services);
    }

    /// <summary>
    /// Adds a singleton message queue instance.
    /// Note: This uses blocking initialization due to DI container constraints.
    /// For async initialization, use IMessageQueueFactory.CreateQueueAsync() directly.
    /// </summary>
    public static IServiceCollection AddSingletonMessageQueue(
        this IServiceCollection services,
        string name)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or whitespace", nameof(name));

        services.AddSingleton<IMessageQueue>(sp =>
        {
            var factory = sp.GetRequiredService<IMessageQueueFactory>();
            // Note: This blocks on async initialization due to DI container constraints.
            // This is acceptable for application startup but not for request-time usage.
            // For async initialization, inject IMessageQueueFactory and call CreateQueueAsync().
            return factory.CreateQueueAsync(name).GetAwaiter().GetResult();
        });

        return services;
    }
}

/// <summary>
/// Builder for configuring message queue providers and middleware
/// </summary>
public interface IMessageQueueBuilder
{
    /// <summary>
    /// The service collection
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Adds a message queue provider
    /// </summary>
    IMessageQueueBuilder AddProvider<TProvider>() where TProvider : class, IMessageQueueProvider;

    /// <summary>
    /// Adds middleware to the message queue pipeline
    /// </summary>
    IMessageQueueBuilder AddMiddleware<TMiddleware>() where TMiddleware : class, IMessageMiddleware;

    /// <summary>
    /// Adds a custom serializer
    /// </summary>
    IMessageQueueBuilder AddSerializer<TSerializer>() where TSerializer : class, IMessageSerializer;
}

/// <summary>
/// Default implementation of IMessageQueueBuilder
/// </summary>
internal sealed class MessageQueueBuilder : IMessageQueueBuilder
{
    public MessageQueueBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }

    public IMessageQueueBuilder AddProvider<TProvider>() where TProvider : class, IMessageQueueProvider
    {
        Services.AddSingleton<IMessageQueueProvider, TProvider>();
        return this;
    }

    public IMessageQueueBuilder AddMiddleware<TMiddleware>() where TMiddleware : class, IMessageMiddleware
    {
        Services.AddSingleton<IMessageMiddleware, TMiddleware>();
        return this;
    }

    public IMessageQueueBuilder AddSerializer<TSerializer>() where TSerializer : class, IMessageSerializer
    {
        Services.Replace(ServiceDescriptor.Singleton<IMessageSerializer, TSerializer>());
        return this;
    }
}
