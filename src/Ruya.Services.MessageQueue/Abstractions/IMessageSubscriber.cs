using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Provides methods for subscribing to messages from a message broker
/// </summary>
public interface IMessageSubscriber
{
    /// <summary>
    /// Subscribes to messages on the specified topic
    /// </summary>
    /// <typeparam name="TMessage">The type of the message</typeparam>
    /// <param name="topic">The topic to subscribe to</param>
    /// <param name="handler">The message handler</param>
    /// <param name="options">Subscription options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A subscription that can be disposed to stop consuming messages</returns>
    Task<IMessageSubscription> SubscribeAsync<TMessage>(
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class;

    /// <summary>
    /// Subscribes to messages on multiple topics
    /// </summary>
    /// <typeparam name="TMessage">The type of the message</typeparam>
    /// <param name="topics">The topics to subscribe to</param>
    /// <param name="handler">The message handler</param>
    /// <param name="options">Subscription options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A subscription that can be disposed to stop consuming messages</returns>
    Task<IMessageSubscription> SubscribeAsync<TMessage>(
        IEnumerable<string> topics,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class;
}

/// <summary>
/// Represents an active message subscription
/// </summary>
public interface IMessageSubscription : IAsyncDisposable
{
    /// <summary>
    /// The subscription ID
    /// </summary>
    string SubscriptionId { get; }

    /// <summary>
    /// The topics being subscribed to
    /// </summary>
    IReadOnlyList<string> Topics { get; }

    /// <summary>
    /// Whether the subscription is active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Pauses message consumption
    /// </summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes message consumption
    /// </summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of message processing
/// </summary>
public sealed class MessageResult
{
    /// <summary>
    /// Message was processed successfully
    /// </summary>
    public static MessageResult Success() => new() { Status = MessageStatus.Success };

    /// <summary>
    /// Message processing failed and should be retried
    /// </summary>
    public static MessageResult Retry(string? reason = null) => new() { Status = MessageStatus.Retry, Reason = reason };

    /// <summary>
    /// Message processing failed and should be rejected (sent to DLQ)
    /// </summary>
    public static MessageResult Reject(string? reason = null) => new() { Status = MessageStatus.Reject, Reason = reason };

    /// <summary>
    /// Message processing status
    /// </summary>
    public required MessageStatus Status { get; init; }

    /// <summary>
    /// Optional reason for the result
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Message processing status
/// </summary>
public enum MessageStatus
{
    /// <summary>
    /// Message was processed successfully
    /// </summary>
    Success,

    /// <summary>
    /// Message processing failed and should be retried
    /// </summary>
    Retry,

    /// <summary>
    /// Message processing failed and should be rejected
    /// </summary>
    Reject
}
