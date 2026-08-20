using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;

namespace Ruya.Services.MessageQueue.Telemetry;

/// <summary>
/// Provider-boundary telemetry for message publication and completed delivery attempts.
/// </summary>
public sealed class MessageQueueTelemetry
{
    public const string InstrumentationName =
        $"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.MessageQueue)}";
    public const string DeliveryAttemptsInstrumentName = "ruya.message_queue.delivery.attempts";
    public const string DeliveryDurationInstrumentName = "ruya.message_queue.delivery.duration";

    private static readonly ActivitySource _activitySource = new(InstrumentationName);
    private static readonly Meter _meter = new(InstrumentationName);
    private static readonly Counter<long> _deliveryAttempts = _meter.CreateCounter<long>(
        DeliveryAttemptsInstrumentName,
        unit: "{attempt}",
        description: "Completed message delivery attempts by mutually exclusive outcome.");
    private static readonly Histogram<double> _deliveryDuration = _meter.CreateHistogram<double>(
        DeliveryDurationInstrumentName,
        unit: "s",
        description: "Elapsed time for completed message delivery attempts.");

    private readonly IOptions<MessageQueueOptions> _options;

    public MessageQueueTelemetry(IOptions<MessageQueueOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public PublishScope<TMessage> StartPublish<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string messagingSystem,
        string destination) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(envelope);
        try
        {
            return new PublishScope<TMessage>(
                _options.Value.EnableTelemetry,
                envelope,
                messagingSystem,
                destination,
                explicitParent: null);
        }
        catch
        {
            return new PublishScope<TMessage>(false, envelope, messagingSystem, destination, explicitParent: null);
        }
    }

    /// <summary>
    /// Starts a publish operation with an explicit parent. Batch providers use this overload so
    /// every message span is a sibling even while the scopes remain open until the batch commits.
    /// </summary>
    public PublishScope<TMessage> StartPublish<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string messagingSystem,
        string destination,
        ActivityContext parentContext) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(envelope);
        try
        {
            return new PublishScope<TMessage>(
                _options.Value.EnableTelemetry,
                envelope,
                messagingSystem,
                destination,
                parentContext);
        }
        catch
        {
            return new PublishScope<TMessage>(false, envelope, messagingSystem, destination, parentContext);
        }
    }

    public DeliveryScope StartDelivery(string messagingSystem, string destination, string? consumerGroup)
    {
        try
        {
            return new DeliveryScope(_options.Value.EnableTelemetry, messagingSystem, destination, consumerGroup);
        }
        catch
        {
            return new DeliveryScope(false, messagingSystem, destination, consumerGroup);
        }
    }

    public sealed class PublishScope<TMessage> : IDisposable where TMessage : class
    {
        private Activity? _activity;
        private int _completed;

        internal PublishScope(
            bool enabled,
            MessageEnvelope<TMessage> envelope,
            string messagingSystem,
            string destination,
            ActivityContext? explicitParent)
        {
            Envelope = envelope;
            if (!enabled)
            {
                return;
            }

            try
            {
                _activity = explicitParent.HasValue
                    ? _activitySource.StartActivity(
                        $"publish {destination}",
                        ActivityKind.Producer,
                        explicitParent.Value)
                    : _activitySource.StartActivity($"publish {destination}", ActivityKind.Producer);

                var traceParent = _activity?.Id;
                var traceState = _activity?.TraceStateString;
                if (traceParent is null && explicitParent.HasValue)
                {
                    traceParent = FormatTraceParent(explicitParent.Value);
                    traceState = explicitParent.Value.TraceState;
                }
                else if (traceParent is null)
                {
                    traceParent = Activity.Current?.Id;
                    traceState = Activity.Current?.TraceStateString;
                }

                if (string.IsNullOrWhiteSpace(traceParent))
                {
                    return;
                }

                var headers = envelope.Headers is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(envelope.Headers, StringComparer.OrdinalIgnoreCase);
                headers["traceparent"] = traceParent;
                if (!string.IsNullOrWhiteSpace(traceState))
                {
                    headers["tracestate"] = traceState!;
                }
                else
                {
                    headers.Remove("tracestate");
                }

                Envelope = CopyEnvelope(envelope, headers);
                SetCommonTags(_activity, messagingSystem, destination);
                _activity?.SetTag("messaging.operation.name", "publish");
                _activity?.SetTag("messaging.message.id", envelope.MessageId);
                _activity?.SetTag("messaging.message.type", envelope.MessageType);
            }
            catch
            {
                TryDisposeActivity();
                _activity = null;
            }
        }

        public MessageEnvelope<TMessage> Envelope { get; }

        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            try
            {
                _activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch
            {
                // Diagnostics must not affect a committed publish.
            }
            finally
            {
                TryDisposeActivity();
            }
        }

        public void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            try
            {
                _activity?.SetTag("error.type", exception.GetType().FullName);
                _activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                _activity?.AddException(exception);
            }
            catch
            {
                // Diagnostics must not mask the publish failure.
            }
            finally
            {
                TryDisposeActivity();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                TryDisposeActivity();
            }
        }

        private void TryDisposeActivity()
        {
            try
            {
                _activity?.Dispose();
            }
            catch
            {
                // Activity listeners are diagnostics and cannot affect queue behavior.
            }
        }

        private static MessageEnvelope<TMessage> CopyEnvelope(
            MessageEnvelope<TMessage> envelope,
            IReadOnlyDictionary<string, string> headers)
        {
            return new MessageEnvelope<TMessage>
            {
                MessageId = envelope.MessageId,
                CorrelationId = envelope.CorrelationId,
                CausationId = envelope.CausationId,
                Source = envelope.Source,
                MessageType = envelope.MessageType,
                Timestamp = envelope.Timestamp,
                Headers = headers,
                Payload = envelope.Payload,
                Priority = envelope.Priority,
                TimeToLive = envelope.TimeToLive,
                DeliveryDelay = envelope.DeliveryDelay,
                Persistent = envelope.Persistent
            };
        }

        private static string? FormatTraceParent(ActivityContext context)
        {
            if (context.TraceId == default || context.SpanId == default)
            {
                return null;
            }

            var flags = (context.TraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
            return $"00-{context.TraceId}-{context.SpanId}-{flags}";
        }
    }

    public sealed class DeliveryScope : IDisposable
    {
        private readonly bool _enabled;
        private readonly string _messagingSystem;
        private readonly string _destination;
        private readonly string? _consumerGroup;
        private readonly long _startedTimestamp;
        private readonly DateTimeOffset _startedAt;
        private Activity? _activity;
        private int _completed;

        internal DeliveryScope(bool enabled, string messagingSystem, string destination, string? consumerGroup)
        {
            _enabled = enabled;
            _messagingSystem = messagingSystem;
            _destination = destination;
            _consumerGroup = consumerGroup;
            _startedTimestamp = Stopwatch.GetTimestamp();
            _startedAt = DateTimeOffset.UtcNow;
        }

        public void AttachEnvelope<TMessage>(MessageEnvelope<TMessage> envelope, int deliveryCount)
            where TMessage : class
        {
            ArgumentNullException.ThrowIfNull(envelope);
            if (!_enabled || _activity is not null)
            {
                return;
            }

            try
            {
                var parent = ExtractParentContext(envelope.Headers);
                _activity = _activitySource.StartActivity(
                    $"consume {_destination}",
                    ActivityKind.Consumer,
                    parent,
                    tags: null,
                    links: null,
                    startTime: _startedAt);
                SetActivityTags(envelope.MessageId, envelope.MessageType, deliveryCount);
            }
            catch
            {
                TryDisposeActivity();
                _activity = null;
            }
        }

        public void Complete(MessageStatus status)
        {
            var outcome = status switch
            {
                MessageStatus.Success => "success",
                MessageStatus.Retry => "retry",
                MessageStatus.Reject => "reject",
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };

            CompleteCore(outcome, null);
        }

        public void Unhandled(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            CompleteCore("unhandled", exception);
        }

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                TryDisposeActivity();
            }
        }

        public void Dispose() => Cancel();

        private void CompleteCore(string outcome, Exception? exception)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            if (!_enabled)
            {
                return;
            }

            try
            {
                EnsureActivity();
                var tags = CreateMetricTags(outcome);
                var elapsedSeconds = Stopwatch.GetElapsedTime(_startedTimestamp).TotalSeconds;
                _deliveryAttempts.Add(1, tags);
                _deliveryDuration.Record(elapsedSeconds, tags);

                _activity?.SetTag("ruya.message_queue.outcome", outcome);
                if (exception is not null)
                {
                    _activity?.SetTag("error.type", exception.GetType().FullName);
                    _activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                    _activity?.AddException(exception);
                }
                else if (outcome == "success")
                {
                    _activity?.SetStatus(ActivityStatusCode.Ok);
                }
            }
            catch
            {
                // Metrics and listeners are diagnostics and cannot affect broker settlement.
            }
            finally
            {
                TryDisposeActivity();
            }
        }

        private void EnsureActivity()
        {
            if (_activity is not null)
            {
                return;
            }

            _activity = _activitySource.StartActivity(
                $"consume {_destination}",
                ActivityKind.Consumer,
                default(ActivityContext),
                tags: null,
                links: null,
                startTime: _startedAt);
            SetActivityTags(null, null, null);
        }

        private void SetActivityTags(string? messageId, string? messageType, int? deliveryCount)
        {
            SetCommonTags(_activity, _messagingSystem, _destination);
            _activity?.SetTag("messaging.operation.name", "process");
            _activity?.SetTag("messaging.consumer.group.name", _consumerGroup);
            _activity?.SetTag("messaging.message.id", messageId);
            _activity?.SetTag("messaging.message.type", messageType);
            _activity?.SetTag("messaging.message.delivery_count", deliveryCount);
        }

        private TagList CreateMetricTags(string outcome)
        {
            var tags = new TagList
            {
                { "messaging.system", _messagingSystem },
                { "messaging.destination.name", _destination },
                { "ruya.message_queue.outcome", outcome }
            };
            if (!string.IsNullOrWhiteSpace(_consumerGroup))
            {
                tags.Add("messaging.consumer.group.name", _consumerGroup);
            }

            return tags;
        }

        private void TryDisposeActivity()
        {
            try
            {
                _activity?.Dispose();
            }
            catch
            {
                // Activity listeners are diagnostics and cannot affect queue behavior.
            }
        }
    }

    private static void SetCommonTags(Activity? activity, string messagingSystem, string destination)
    {
        activity?.SetTag("messaging.system", messagingSystem);
        activity?.SetTag("messaging.destination.name", destination);
    }

    private static ActivityContext ExtractParentContext(IReadOnlyDictionary<string, string>? headers)
    {
        if (!TryGetHeader(headers, "traceparent", out var traceParent))
        {
            return default;
        }

        TryGetHeader(headers, "tracestate", out var traceState);
        return ActivityContext.TryParse(traceParent, traceState, out var parent) ? parent : default;
    }

    private static bool TryGetHeader(
        IReadOnlyDictionary<string, string>? headers,
        string name,
        out string? value)
    {
        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}
