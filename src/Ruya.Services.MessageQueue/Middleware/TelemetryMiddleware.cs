using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.Middleware;

/// <summary>
/// Middleware for OpenTelemetry tracing and metrics
/// </summary>
public sealed class TelemetryMiddleware : MessageMiddleware
{
    private readonly ILogger<TelemetryMiddleware> _logger;
    private static readonly ActivitySource _activitySource = new("Ruya.Services.MessageQueue", "1.0.0");

    public TelemetryMiddleware(ILogger<TelemetryMiddleware> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task<string> PublishAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        Func<MessageEnvelope<TMessage>, string, Task<string>> next,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(
            "message.publish",
            ActivityKind.Producer);

        activity.SetTag("messaging.system", "Ruya.Services.MessageQueue");
        activity.SetTag("messaging.destination", topic);
        activity.SetTag("messaging.message_id", envelope.MessageId);
        activity.SetTag("messaging.message_type", envelope.MessageType);
        activity.SetTag("messaging.correlation_id", envelope.CorrelationId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Publishing message {MessageId} of type {MessageType} to topic {Topic}",
                envelope.MessageId,
                envelope.MessageType,
                topic);

            var messageId = await next(envelope, topic);

            stopwatch.Stop();

            _logger.LogInformation(
                "Published message {MessageId} to topic {Topic} in {ElapsedMs}ms",
                messageId,
                topic,
                stopwatch.ElapsedMilliseconds);

            activity.SetTag("messaging.operation", "publish");
            activity.SetStatus(ActivityStatusCode.Ok);

            return messageId;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Failed to publish message {MessageId} to topic {Topic} after {ElapsedMs}ms",
                envelope.MessageId,
                topic,
                stopwatch.ElapsedMilliseconds);

            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.RecordException(ex);

            throw;
        }
    }

    public override async Task<MessageResult> ConsumeAsync<TMessage>(
        MessageContext<TMessage> context,
        Func<MessageContext<TMessage>, Task<MessageResult>> next,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(
            "message.consume",
            ActivityKind.Consumer,
            parentContext: ExtractParentContext(context));

        activity.SetTag("messaging.system", "Ruya.Services.MessageQueue");
        activity.SetTag("messaging.destination", context.Topic);
        activity.SetTag("messaging.message_id", context.Envelope.MessageId);
        activity.SetTag("messaging.message_type", context.Envelope.MessageType);
        activity.SetTag("messaging.correlation_id", context.Envelope.CorrelationId);
        activity.SetTag("messaging.consumer_group", context.ConsumerGroup);
        activity.SetTag("messaging.delivery_count", context.DeliveryCount);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Processing message {MessageId} of type {MessageType} from topic {Topic} (Attempt {DeliveryCount})",
                context.Envelope.MessageId,
                context.Envelope.MessageType,
                context.Topic,
                context.DeliveryCount);

            var result = await next(context);

            stopwatch.Stop();

            var logLevel = result.Status == MessageStatus.Success ? LogLevel.Information : LogLevel.Warning;
            _logger.Log(
                logLevel,
                "Processed message {MessageId} from topic {Topic} with status {Status} in {ElapsedMs}ms. Reason: {Reason}",
                context.Envelope.MessageId,
                context.Topic,
                result.Status,
                stopwatch.ElapsedMilliseconds,
                result.Reason);

            activity.SetTag("messaging.operation", "consume");
            activity.SetTag("messaging.result", result.Status.ToString());
            activity.SetStatus(result.Status == MessageStatus.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Failed to process message {MessageId} from topic {Topic} after {ElapsedMs}ms",
                context.Envelope.MessageId,
                context.Topic,
                stopwatch.ElapsedMilliseconds);

            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.RecordException(ex);

            throw;
        }
    }

    private static ActivityContext ExtractParentContext<TMessage>(MessageContext<TMessage> context) where TMessage : class
    {
        // Try to extract trace context from message headers if available
        if (context.Envelope.Headers != null &&
            context.Envelope.Headers.TryGetValue("traceparent", out var traceParent))
        {
            if (ActivityContext.TryParse(traceParent, null, out var parentContext))
            {
                return parentContext;
            }
        }

        return default;
    }
}
