using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Represents a message queue provider implementation (e.g., RabbitMQ, Redis)
/// </summary>
public interface IMessageQueueProvider
{
    /// <summary>
    /// The provider name (e.g., "RabbitMQ", "Redis")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the capabilities supported by this provider
    /// </summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Creates a message queue instance
    /// </summary>
    Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes the capabilities of a message queue provider
/// </summary>
public sealed class ProviderCapabilities
{
    /// <summary>
    /// Supports native message priority
    /// </summary>
    public bool SupportsPriority { get; init; }

    /// <summary>
    /// Supports delayed delivery
    /// </summary>
    public bool SupportsDelayedDelivery { get; init; }

    /// <summary>
    /// Supports message TTL
    /// </summary>
    public bool SupportsTimeToLive { get; init; }

    /// <summary>
    /// Supports publisher confirms
    /// </summary>
    public bool SupportsPublisherConfirms { get; init; }

    /// <summary>
    /// Supports consumer groups
    /// </summary>
    public bool SupportsConsumerGroups { get; init; }

    /// <summary>
    /// Supports dead letter queues
    /// </summary>
    public bool SupportsDeadLetterQueue { get; init; }

    /// <summary>
    /// Supports message replay (streams/event sourcing)
    /// </summary>
    public bool SupportsReplay { get; init; }

    /// <summary>
    /// Supports batch publishing
    /// </summary>
    public bool SupportsBatchPublish { get; init; }

    /// <summary>
    /// Supports transactions (durable messaging)
    /// </summary>
    public bool SupportsTransactions { get; init; }

    /// <summary>
    /// Maximum priority level supported (e.g., 255 for byte-based priority)
    /// Null if priority is not supported or unlimited
    /// </summary>
    public int? MaxPriorityLevel { get; init; }

    /// <summary>
    /// Additional provider-specific capabilities
    /// </summary>
    public IReadOnlyDictionary<string, bool>? AdditionalCapabilities { get; init; }
}
