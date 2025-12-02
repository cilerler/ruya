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
    /// Global timeout for operations
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
    /// Connection string or configuration
    /// </summary>
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
        if (options.DefaultTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("DefaultTimeout must be greater than zero");
        }

        if (!string.IsNullOrEmpty(options.DefaultProvider) &&
            !options.Providers.ContainsKey(options.DefaultProvider))
        {
            return ValidateOptionsResult.Fail($"DefaultProvider '{options.DefaultProvider}' is not configured in Providers");
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
