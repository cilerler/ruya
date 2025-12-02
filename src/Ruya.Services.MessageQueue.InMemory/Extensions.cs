using System;
using Microsoft.Extensions.DependencyInjection;
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
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        // Configure options
        if (configureOptions != null)
        {
            builder.Services.Configure(configureOptions);
        }
        else
        {
            // Register default options
            builder.Services.Configure<InMemoryOptions>(_ => { });
        }

        // Register provider
        builder.AddProvider<InMemoryProvider>();

        return builder;
    }
}
