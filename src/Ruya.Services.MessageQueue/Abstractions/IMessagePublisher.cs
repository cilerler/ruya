using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Provides methods for publishing messages to a message broker
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message to the specified topic
    /// </summary>
    /// <typeparam name="TMessage">The type of the message</typeparam>
    /// <param name="topic">The topic to publish to</param>
    /// <param name="message">The message to publish</param>
    /// <param name="options">Publishing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message ID</returns>
    Task<string> PublishAsync<TMessage>(
        string topic,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class;

    /// <summary>
    /// Publishes multiple messages in a batch
    /// </summary>
    /// <typeparam name="TMessage">The type of the messages</typeparam>
    /// <param name="topic">The topic to publish to</param>
    /// <param name="messages">The messages to publish</param>
    /// <param name="options">Publishing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The message IDs</returns>
    Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
        string topic,
        IEnumerable<TMessage> messages,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class;

    /// <summary>
    /// Creates a fluent builder for publishing messages
    /// </summary>
    /// <typeparam name="TMessage">The type of the message</typeparam>
    /// <param name="topic">The topic to publish to</param>
    /// <returns>A message publisher builder</returns>
    IMessagePublisherBuilder<TMessage> To<TMessage>(string topic) where TMessage : class;
}

/// <summary>
/// Fluent builder for publishing messages
/// </summary>
/// <typeparam name="TMessage">The type of the message</typeparam>
public interface IMessagePublisherBuilder<TMessage> where TMessage : class
{
    /// <summary>
    /// Sets the message priority
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithPriority(byte priority);

    /// <summary>
    /// Sets the time-to-live for the message
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithTimeToLive(TimeSpan ttl);

    /// <summary>
    /// Sets the delivery delay for the message
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithDelay(TimeSpan delay);

    /// <summary>
    /// Sets whether the message should be persisted
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithPersistence(bool persistent = true);

    /// <summary>
    /// Sets the correlation ID
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithCorrelationId(string correlationId);

    /// <summary>
    /// Sets the causation ID
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithCausationId(string causationId);

    /// <summary>
    /// Sets the source
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithSource(string source);

    /// <summary>
    /// Adds a custom header
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithHeader(string key, string value);

    /// <summary>
    /// Adds multiple custom headers
    /// </summary>
    IMessagePublisherBuilder<TMessage> WithHeaders(IDictionary<string, string> headers);

    /// <summary>
    /// Publishes the message
    /// </summary>
    Task<string> SendAsync(TMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes multiple messages
    /// </summary>
    Task<IReadOnlyList<string>> SendBatchAsync(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default);
}
