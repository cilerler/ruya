using System;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Factory;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Telemetry;

namespace Ruya.Services.MessageQueue.Extensions;

/// <summary>
/// Extension methods for configuring Ruya.Services.MessageQueue services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Ruya.Services.MessageQueue services using an explicitly supplied configuration root.
    /// </summary>
    [Obsolete("Use AddMessageQueue() and configure the 'MessageQueue' section. The IConfiguration overload will be removed in version 9.0.")]
    public static IMessageQueueBuilder AddMessageQueue(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddMessageQueue(options =>
            configuration.GetSection(MessageQueueOptions.ConfigurationSectionName).Bind(options));
    }

    /// <summary>
    /// Adds Ruya.Services.MessageQueue services, binds <see cref="MessageQueueOptions"/>
    /// from <see cref="MessageQueueOptions.ConfigurationSectionName"/>, and validates the
    /// resulting options when the host starts.
    /// </summary>
    public static IMessageQueueBuilder AddMessageQueue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MessageQueueOptions>()
            .BindConfiguration(MessageQueueOptions.ConfigurationSectionName)
            .ValidateOnStart();

        return AddMessageQueueCore(services);
    }

    /// <summary>
    /// Adds Ruya.Services.MessageQueue services to the service collection
    /// </summary>
    public static IMessageQueueBuilder AddMessageQueue(
        this IServiceCollection services,
        Action<MessageQueueOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddOptions<MessageQueueOptions>()
            .Configure(configureOptions)
            .ValidateOnStart();

        return AddMessageQueueCore(services);
    }

    /// <summary>
    /// Registers a compatibility singleton proxy for a named queue without blocking DI resolution.
    /// </summary>
    [Obsolete("Inject IMessageQueueFactory and await CreateQueueAsync(name). This compatibility proxy will be removed in version 9.0.")]
    public static IServiceCollection AddSingletonMessageQueue(
        this IServiceCollection services,
        string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.AddSingleton<IMessageQueue>(serviceProvider =>
        {
            var options = serviceProvider.GetService<IOptions<MessageQueueOptions>>()?.Value;
            var configuredProvider = options?.Providers.TryGetValue(name, out var provider) == true
                ? provider.Type
                : null;

            return new AsyncLazyMessageQueue(
                name,
                configuredProvider,
                serviceProvider.GetRequiredService<IMessageQueueFactory>());
        });

        return services;
    }

    private static IMessageQueueBuilder AddMessageQueueCore(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MessageQueueOptions>, MessageQueueOptionsValidator>());

        // Register core services
        services.TryAddSingleton<IMessageJsonTypeInfoResolver>(serviceProvider =>
            new MessageJsonTypeInfoResolver(
                serviceProvider.GetServices<JsonSerializerContext>()));
        services.TryAddSingleton<IMessageSerializer>(serviceProvider =>
            new JsonMessageSerializer(
                serviceProvider.GetServices<JsonSerializerContext>(),
                serviceProvider.GetRequiredService<IMessageJsonTypeInfoResolver>()));
        services.TryAddSingleton<IMessageQueueFactory, MessageQueueFactory>();
        services.TryAddSingleton<MessageQueueTelemetry>();

        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(MessageQueueTelemetryRegistration)))
        {
            services.AddSingleton<MessageQueueTelemetryRegistration>();
            services.AddOpenTelemetry()
                .WithTracing(builder => builder.AddSource(MessageQueueTelemetry.InstrumentationName))
                .WithMetrics(builder => builder.AddMeter(MessageQueueTelemetry.InstrumentationName));
        }

        return new MessageQueueBuilder(services);
    }

    /// <summary>
    /// Registers producer-owned source-generated JSON metadata for message payload contracts.
    /// </summary>
    /// <remarks>
    /// Register the producer-owned context instance (for example,
    /// <c>OrderContractsJsonSerializerContext.Default</c>). The default
    /// <see cref="JsonMessageSerializer"/> uses registered contexts before its
    /// infrastructure-only reflection fallback. Once at least one context is registered, every
    /// application payload serialized inside a <see cref="MessageEnvelope{TMessage}"/> must be
    /// covered by one of the registered contexts; missing metadata fails explicitly.
    /// Contexts are queried in registration order, so register overlapping contract metadata once.
    /// Custom <see cref="IMessageSerializer"/> implementations own their own metadata contract.
    /// </remarks>
    public static IMessageQueueBuilder AddJsonSerializerContext(
        this IMessageQueueBuilder builder,
        JsonSerializerContext context)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (context == null) throw new ArgumentNullException(nameof(context));

        builder.Services.AddSingleton(context);
        return builder;
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
        // Telemetry is automatic at the provider boundary. Retain this source-compatible call as a
        // no-op so older applications do not create a second producer/consumer instrumentation layer.
        if (typeof(TMiddleware) != typeof(TelemetryMiddleware))
        {
            Services.TryAddEnumerable(ServiceDescriptor.Singleton<IMessageMiddleware, TMiddleware>());
        }

        return this;
    }

    public IMessageQueueBuilder AddSerializer<TSerializer>() where TSerializer : class, IMessageSerializer
    {
        Services.Replace(ServiceDescriptor.Singleton<IMessageSerializer, TSerializer>());
        return this;
    }
}

internal sealed class MessageQueueTelemetryRegistration;
