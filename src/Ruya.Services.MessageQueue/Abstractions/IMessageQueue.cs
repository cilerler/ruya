using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Represents a message queue that provides both publishing and subscribing capabilities
/// Follows the Microsoft ILogger pattern
/// </summary>
public interface IMessageQueue : IMessagePublisher, IMessageSubscriber, IAsyncDisposable
{
    /// <summary>
    /// The name of this message queue instance
    /// Used for multi-provider scenarios (e.g., "rabbitmq", "redis")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The provider type (e.g., "RabbitMQ", "Redis", "AzureServiceBus")
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Checks if the message queue is healthy and connected
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating named message queue instances
/// Follows the Microsoft ILoggerFactory pattern
/// </summary>
public interface IMessageQueueFactory : IAsyncDisposable
{
    /// <summary>
    /// Creates a message queue instance with the specified name asynchronously
    /// </summary>
    /// <param name="name">The name of the message queue instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A message queue instance</returns>
    Task<IMessageQueue> CreateQueueAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registered provider names
    /// </summary>
    IReadOnlyList<string> GetRegisteredProviders();
}
