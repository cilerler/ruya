using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;

namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// Extension methods for registering the InMemory message bus provider
/// </summary>
public static class InMemoryExtensions
{
    /// <summary>
    /// Adds the InMemory provider to the message queue
    /// </summary>
    /// <param name="builder">The message queue builder</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>The builder for chaining</returns>
    public static IMessageQueueBuilder AddInMemoryProvider(
        this IMessageQueueBuilder builder,
        Action<InMemoryOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services
            .AddOptions<InMemoryOptions>()
            .BindConfiguration(InMemoryOptions.ConfigurationSectionName)
            .ValidateOnStart();

        if (configureOptions is not null)
        {
            optionsBuilder.Configure(configureOptions);
        }

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<InMemoryOptions>, InMemoryOptionsValidator>());

        builder.Services.TryAddSingleton<IInMemoryDeadLetterStore, InMemoryDeadLetterStore>();

        builder.AddProvider<InMemoryProvider>();

        return builder;
    }
}
