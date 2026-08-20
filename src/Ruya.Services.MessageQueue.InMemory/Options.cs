using System;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// Configuration options for the in-memory message bus provider
/// </summary>
public sealed class InMemoryOptions
{
    public const string ConfigurationSectionName =
        $"{nameof(Ruya.Services.MessageQueue)}:{nameof(Ruya.Services.MessageQueue.InMemory)}";

    /// <summary>
    /// Maximum capacity per topic channel (default: unbounded)
    /// </summary>
    public int? ChannelCapacity { get; set; }

    /// <summary>
    /// Whether to enable dead letter queue for failed messages (default: true)
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = true;

    /// <summary>
    /// Maximum retained dead-letter messages per named in-memory queue (default: 1000).
    /// When the capacity is reached, the oldest retained message is discarded.
    /// </summary>
    public int DeadLetterQueueCapacity { get; set; } = 1000;

    /// <summary>
    /// Maximum delivery attempts, including the initial delivery, before sending to the DLQ
    /// (default: 3). Used when a subscription does not specify
    /// <see cref="SubscribeOptions.MaxDeliveryCount"/> or
    /// <see cref="SubscribeOptions.RetryPolicy"/>.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Fixed delay between delivery attempts (default: 1 second). Used when a subscription does
    /// not specify <see cref="SubscribeOptions.RetryPolicy"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to persist messages in memory for replay (default: false)
    /// Use with caution - can consume significant memory
    /// </summary>
    public bool EnableMessageStore { get; set; } = false;

    /// <summary>
    /// Maximum number of messages to store per topic when EnableMessageStore is true (default: 1000)
    /// </summary>
    public int MaxStoredMessagesPerTopic { get; set; } = 1000;

    /// <summary>
    /// Compatibility placeholder for the former priority setting. In-memory channels preserve
    /// enqueue order and do not provide priority delivery.
    /// </summary>
    [Obsolete("In-memory priority delivery is not supported. Use a provider with native priority support. This property will be removed in version 9.0.")]
    public bool EnablePriority { get; set; } = true;

    /// <summary>
    /// Whether to throw exceptions for invalid operations or log warnings (default: throw)
    /// </summary>
    public bool ThrowOnError { get; set; } = true;
}

internal sealed class InMemoryOptionsValidator : IValidateOptions<InMemoryOptions>
{
    public ValidateOptionsResult Validate(string? name, InMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ChannelCapacity is <= 0)
        {
            return ValidateOptionsResult.Fail("ChannelCapacity must be greater than zero when configured.");
        }

        if (options.MaxRetryAttempts < 1)
        {
            return ValidateOptionsResult.Fail("MaxRetryAttempts must be at least one.");
        }

        if (options.DeadLetterQueueCapacity < 1)
        {
            return ValidateOptionsResult.Fail("DeadLetterQueueCapacity must be at least one.");
        }

        if (options.MaxRetryAttempts > 1 && options.RetryDelay <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("RetryDelay must be greater than zero when retries are enabled.");
        }

        if (options.MaxStoredMessagesPerTopic < 1)
        {
            return ValidateOptionsResult.Fail("MaxStoredMessagesPerTopic must be at least one.");
        }

        return ValidateOptionsResult.Success;
    }
}
