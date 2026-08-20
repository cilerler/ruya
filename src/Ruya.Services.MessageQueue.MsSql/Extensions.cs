using System;
using Microsoft.Extensions.Configuration;
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
    /// Adds SQL Server Service Broker options bound from <c>MessageQueue:MsSql</c>.
    /// </summary>
    public static IMessageQueueBuilder AddMsSql(this IMessageQueueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services
            .AddOptions<MsSqlOptions>()
            .BindConfiguration(MsSqlOptions.ConfigurationSectionName)
            .ValidateOnStart();
        RegisterProvider(builder);
        return builder;
    }

    /// <summary>
    /// Adds SQL Server Service Broker as a message queue provider
    /// </summary>
    public static IMessageQueueBuilder AddMsSql(
        this IMessageQueueBuilder builder,
        Action<MsSqlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services
            .AddOptions<MsSqlOptions>()
            .Configure(configure)
            .ValidateOnStart();
        RegisterProvider(builder);

        return builder;
    }

    private static void RegisterProvider(IMessageQueueBuilder builder)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MsSqlOptions>, MsSqlOptionsValidator>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MsSqlOptions>, MsSqlConnectionStringCatalogValidator>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<MsSqlOptions>, MsSqlConnectionStringResolver>());

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IMessageQueueProvider, MsSqlProvider>());
    }

    internal static void ResolveConnectionString(MsSqlOptions options, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(options.MessageQueueConnectionStringKey) ||
            options.ConnectionStringResolvedFromCatalog)
        {
            return;
        }

        var resolvedConnectionString = configuration.GetConnectionString(options.MessageQueueConnectionStringKey);
        options.ConnectionString = resolvedConnectionString ?? string.Empty;
        options.ConnectionStringResolvedFromCatalog = !string.IsNullOrWhiteSpace(resolvedConnectionString);
    }
}

internal sealed class MsSqlConnectionStringResolver : IConfigureOptions<MsSqlOptions>
{
    private readonly IServiceProvider _serviceProvider;

    public MsSqlConnectionStringResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void Configure(MsSqlOptions options)
    {
        var configuration = _serviceProvider.GetService<IConfiguration>();
        if (configuration is not null)
        {
            MsSqlExtensions.ResolveConnectionString(options, configuration);
        }
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

#pragma warning disable CS0618 // Compatibility property must remain validated until version 9.0.
        if (options.EnableConversationPooling)
        {
            return ValidateOptionsResult.Fail(
                "EnableConversationPooling is not supported by the current Service Broker provider. Leave it disabled.");
        }
#pragma warning restore CS0618

        return ValidateOptionsResult.Success;
    }
}

internal sealed class MsSqlConnectionStringCatalogValidator : IValidateOptions<MsSqlOptions>
{
    public ValidateOptionsResult Validate(string? name, MsSqlOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.MessageQueueConnectionStringKey))
        {
            return ValidateOptionsResult.Skip;
        }

        return !options.ConnectionStringResolvedFromCatalog
            ? ValidateOptionsResult.Fail(
                "MessageQueueConnectionStringKey must identify a configured connection string.")
            : ValidateOptionsResult.Success;
    }
}
