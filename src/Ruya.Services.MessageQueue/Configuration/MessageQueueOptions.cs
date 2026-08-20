using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Ruya.Services.MessageQueue.Configuration;

/// <summary>
/// Global options for the message queue
/// </summary>
public sealed class MessageQueueOptions
{
    /// <summary>
    /// Configuration section used by the parameterless registration overload.
    /// </summary>
    public const string ConfigurationSectionName = nameof(Ruya.Services.MessageQueue);

    /// <summary>
    /// Default provider to use when no provider is specified
    /// </summary>
    public string? DefaultProvider { get; set; }

    /// <summary>
    /// Whether to enable telemetry (OpenTelemetry)
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// Whether to enable health checks
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// Default serializer to use
    /// </summary>
    public string Serializer { get; set; } = "json";

    /// <summary>
    /// Default timeout applied to finite queue operations such as creation, publishing, and
    /// health checks. Subscription lifetime tokens are forwarded unchanged so host shutdown can
    /// continue to cancel handlers after setup completes.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Provider-specific configurations
    /// </summary>
    public Dictionary<string, ProviderConfiguration> Providers { get; set; } = new();
}

/// <summary>
/// Base configuration for a message queue provider
/// </summary>
public class ProviderConfiguration
{
    /// <summary>
    /// Whether this provider is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Provider type (e.g., "RabbitMQ", "Redis")
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Released 8.x compatibility placeholder. Provider credentials are resolved by each provider
    /// from its descriptive connection-string catalog key; this value is never consumed.
    /// </summary>
    [Obsolete("ProviderConfiguration.ConnectionString is not consumed. Configure the provider-specific *ConnectionStringKey and supply its value through ConnectionStrings and secrets. This property will be removed in version 9.0.")]
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Additional provider-specific settings
    /// </summary>
    public Dictionary<string, object>? Settings { get; set; }
}

/// <summary>
/// Validates MessageQueueOptions
/// </summary>
public sealed class MessageQueueOptionsValidator : IValidateOptions<MessageQueueOptions>
{
    public ValidateOptionsResult Validate(string? name, MessageQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DefaultTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("DefaultTimeout must be greater than zero");
        }

        if (!string.Equals(options.Serializer, "json", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                $"Serializer '{options.Serializer}' is not supported. The named Serializer setting " +
                "supports only 'json'; custom IMessageSerializer implementations are registered " +
                "independently with AddSerializer<TSerializer>().");
        }

        if (!string.IsNullOrEmpty(options.DefaultProvider) &&
            !options.Providers.ContainsKey(options.DefaultProvider))
        {
            return ValidateOptionsResult.Fail($"DefaultProvider '{options.DefaultProvider}' is not configured in Providers");
        }

#pragma warning disable CS0618 // Released compatibility property must fail safely until version 9.0.
        var providerWithInlineConnection = options.Providers.FirstOrDefault(
            static provider => !string.IsNullOrWhiteSpace(provider.Value.ConnectionString));
#pragma warning restore CS0618
        if (providerWithInlineConnection.Value is not null)
        {
            return ValidateOptionsResult.Fail(
                $"Provider '{providerWithInlineConnection.Key}' sets the obsolete " +
                "ProviderConfiguration.ConnectionString value, which is not consumed. Remove it, " +
                "configure that provider's descriptive *ConnectionStringKey, and supply the resolved " +
                "value through ConnectionStrings and secrets.");
        }

        foreach (var provider in options.Providers.Where(p => p.Value.Enabled))
        {
            if (string.IsNullOrWhiteSpace(provider.Value.Type))
            {
                return ValidateOptionsResult.Fail($"Provider '{provider.Key}' must have a Type specified");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
