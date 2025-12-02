using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// Extension methods for configuring SQL Server Service Broker message bus
/// </summary>
public static class MsSqlExtensions
{
    /// <summary>
    /// Adds SQL Server Service Broker as a message queue provider
    /// </summary>
    public static IMessageQueueBuilder AddMsSql(
        this IMessageQueueBuilder builder,
        Action<MsSqlOptions> configure)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        // Register options
        builder.Services.Configure(configure);

        // Validate options
        builder.Services.AddSingleton<IValidateOptions<MsSqlOptions>, MsSqlOptionsValidator>();

        // Register provider
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IMessageQueueProvider, MsSqlProvider>());

        return builder;
    }
}

/// <summary>
/// Validates MsSqlOptions configuration
/// </summary>
internal sealed class MsSqlOptionsValidator : IValidateOptions<MsSqlOptions>
{
    public ValidateOptionsResult Validate(string? name, MsSqlOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail("ConnectionString is required for SQL Server Service Broker");
        }

        if (options.ReceiveTimeoutMs < 0)
        {
            return ValidateOptionsResult.Fail("ReceiveTimeoutMs must be >= 0");
        }

        if (options.BatchSize < 1)
        {
            return ValidateOptionsResult.Fail("BatchSize must be at least 1");
        }

        if (options.MaxDeliveryAttempts < 1)
        {
            return ValidateOptionsResult.Fail("MaxDeliveryAttempts must be at least 1");
        }

        if (options.CommandTimeoutSeconds < 1)
        {
            return ValidateOptionsResult.Fail("CommandTimeoutSeconds must be at least 1");
        }

        if (options.PollingIntervalMs < 10)
        {
            return ValidateOptionsResult.Fail("PollingIntervalMs must be at least 10ms");
        }

        if (options.EnableConversationPooling && options.MaxPooledConversations < 1)
        {
            return ValidateOptionsResult.Fail("MaxPooledConversations must be at least 1 when conversation pooling is enabled");
        }

        return ValidateOptionsResult.Success;
    }
}
