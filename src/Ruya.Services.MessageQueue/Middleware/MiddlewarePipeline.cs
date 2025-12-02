using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.Middleware;

/// <summary>
/// Builds and executes middleware pipeline for message publishing and consuming
/// </summary>
public sealed class MiddlewarePipeline
{
    private readonly IReadOnlyList<IMessageMiddleware> _middlewares;

    public MiddlewarePipeline(IEnumerable<IMessageMiddleware> middlewares)
    {
        _middlewares = middlewares?.ToList() ?? new List<IMessageMiddleware>();
    }

    /// <summary>
    /// Executes the publish pipeline
    /// </summary>
    public Task<string> ExecutePublishAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        Func<MessageEnvelope<TMessage>, string, Task<string>> finalHandler,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        if (_middlewares.Count == 0)
        {
            return finalHandler(envelope, topic);
        }

        Task<string> Next(MessageEnvelope<TMessage> env, string t, int currentIndex)
        {
            if (currentIndex >= _middlewares.Count)
            {
                return finalHandler(env, t);
            }

            var middleware = _middlewares[currentIndex];
            return middleware.PublishAsync(env, t, (e, tp) => Next(e, tp, currentIndex + 1), cancellationToken);
        }

        return Next(envelope, topic, 0);
    }

    /// <summary>
    /// Executes the consume pipeline
    /// </summary>
    public Task<MessageResult> ExecuteConsumeAsync<TMessage>(
        MessageContext<TMessage> context,
        Func<MessageContext<TMessage>, Task<MessageResult>> finalHandler,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        if (_middlewares.Count == 0)
        {
            return finalHandler(context);
        }

        Task<MessageResult> Next(MessageContext<TMessage> ctx, int currentIndex)
        {
            if (currentIndex >= _middlewares.Count)
            {
                return finalHandler(ctx);
            }

            var middleware = _middlewares[currentIndex];
            return middleware.ConsumeAsync(ctx, c => Next(c, currentIndex + 1), cancellationToken);
        }

        return Next(context, 0);
    }
}
