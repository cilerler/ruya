using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.Middleware;

/// <summary>
/// Compatibility shim for applications that previously registered telemetry as middleware.
/// Message queue telemetry is now emitted automatically at the provider boundary.
/// </summary>
public sealed class TelemetryMiddleware : MessageMiddleware
{
    /// <summary>
    /// Creates the no-op compatibility middleware.
    /// </summary>
    public TelemetryMiddleware()
    {
    }

    /// <summary>
    /// Creates the no-op compatibility middleware using the former 8.x constructor shape.
    /// </summary>
    [Obsolete("Message queue telemetry is emitted automatically at the provider boundary. Remove explicit TelemetryMiddleware registration; this constructor will be removed in version 9.0.")]
    public TelemetryMiddleware(ILogger<TelemetryMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
    }

    public override Task<string> PublishAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        Func<MessageEnvelope<TMessage>, string, Task<string>> next,
        CancellationToken cancellationToken = default)
    {
        return next(envelope, topic);
    }

    public override Task<MessageResult> ConsumeAsync<TMessage>(
        MessageContext<TMessage> context,
        Func<MessageContext<TMessage>, Task<MessageResult>> next,
        CancellationToken cancellationToken = default)
    {
        return next(context);
    }
}
