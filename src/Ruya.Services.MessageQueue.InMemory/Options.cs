using System;

namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// Configuration options for the in-memory message bus provider
/// </summary>
public sealed class InMemoryOptions
{
    /// <summary>
    /// Maximum capacity per topic channel (default: unbounded)
    /// </summary>
    public int? ChannelCapacity { get; set; }

    /// <summary>
    /// Whether to enable dead letter queue for failed messages (default: true)
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = true;

    /// <summary>
    /// Maximum retry attempts before sending to DLQ (default: 3)
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts (default: 1 second)
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
    /// Whether to support message priority (default: true)
    /// </summary>
    public bool EnablePriority { get; set; } = true;

    /// <summary>
    /// Whether to throw exceptions for invalid operations or log warnings (default: throw)
    /// </summary>
    public bool ThrowOnError { get; set; } = true;
}
