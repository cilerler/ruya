using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Middleware for processing messages in a pipeline
/// </summary>
public interface IMessageMiddleware
{
    /// <summary>
    /// Executes the middleware for publishing
    /// </summary>
    Task<string> PublishAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        Func<MessageEnvelope<TMessage>, string, Task<string>> next,
        CancellationToken cancellationToken = default) where TMessage : class;

    /// <summary>
    /// Executes the middleware for consuming
    /// </summary>
    Task<MessageResult> ConsumeAsync<TMessage>(
        MessageContext<TMessage> context,
        Func<MessageContext<TMessage>, Task<MessageResult>> next,
        CancellationToken cancellationToken = default) where TMessage : class;
}

/// <summary>
/// Base class for message middleware
/// </summary>
public abstract class MessageMiddleware : IMessageMiddleware
{
    /// <inheritdoc />
    public virtual Task<string> PublishAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        Func<MessageEnvelope<TMessage>, string, Task<string>> next,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        return next(envelope, topic);
    }

    /// <inheritdoc />
    public virtual Task<MessageResult> ConsumeAsync<TMessage>(
        MessageContext<TMessage> context,
        Func<MessageContext<TMessage>, Task<MessageResult>> next,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        return next(context);
    }
}
