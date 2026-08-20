using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Telemetry;

using System.Collections.Concurrent;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Security.Cryptography;

namespace Ruya.Services.MessageQueue.RabbitMq;

/// <summary>
/// RabbitMQ implementation of IMessageQueue
/// </summary>
internal sealed class RabbitMQMessageQueue : IMessageQueue
{
    private static readonly EventId QueueInitialized = new(1000, nameof(QueueInitialized));
    private static readonly EventId ConfirmedBatchPublished = new(1001, nameof(ConfirmedBatchPublished));
    private static readonly EventId UnconfirmedBatchPublished = new(1002, nameof(UnconfirmedBatchPublished));
    private static readonly EventId SubscriptionStarted = new(1003, nameof(SubscriptionStarted));
    private static readonly EventId DeliveryWithoutIdentityRejected = new(1004, nameof(DeliveryWithoutIdentityRejected));
    private static readonly EventId AutoAcknowledgedDeliveryFailed = new(1005, nameof(AutoAcknowledgedDeliveryFailed));
    private static readonly EventId DeliveryRetrying = new(1006, nameof(DeliveryRetrying));
    private static readonly EventId PoisonDeliveryRejected = new(1007, nameof(PoisonDeliveryRejected));
    private static readonly EventId DeliveryLimitReached = new(1008, nameof(DeliveryLimitReached));
    private static readonly EventId DeadLetterTopologyCreated = new(1009, nameof(DeadLetterTopologyCreated));
    private static readonly EventId DeadLetterTopologyCreationFailed = new(1010, nameof(DeadLetterTopologyCreationFailed));
    private static readonly EventId ConnectionCloseFailed = new(1011, nameof(ConnectionCloseFailed));
    private static readonly EventId QueueDisposed = new(1012, nameof(QueueDisposed));

    private readonly string _name;
    private readonly RabbitMQOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly MessageQueueTelemetry _telemetry;
    private readonly ILogger _logger;
    private readonly ChannelPool _defaultChannelPool;
    private readonly ChannelPool? _unconfirmedChannelPool;
    private readonly ConcurrentDictionary<DeliveryAttemptKey, int> _deliveryAttempts = new();
    private volatile IConnection? _connection;
    private volatile bool _disposed;

    public RabbitMQMessageQueue(
        string name,
        IConnection connection,
        RabbitMQOptions options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        MessageQueueTelemetry telemetry,
        ILogger logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _defaultChannelPool = new ChannelPool(
            _connection,
            _options.ChannelPoolSize,
            _options.UsePublisherConfirms,
            _logger);
        _unconfirmedChannelPool = _options.UsePublisherConfirms
            ? new ChannelPool(_connection, _options.ChannelPoolSize, enablePublisherConfirms: false, _logger)
            : null;


        _logger.LogInformation(QueueInitialized, "RabbitMQ message bus '{Name}' initialized", _name);
    }

    public string Name => _name;
    public string Provider => "RabbitMQ";

    public async Task<string> PublishAsync<TMessage>(
        string topic,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var telemetry = _telemetry.StartPublish(CreateEnvelope(message, options), "rabbitmq", topic);
        try
        {
            var messageId = await _pipeline.ExecutePublishAsync(
                telemetry.Envelope,
                topic,
                async (env, t) => await PublishInternalAsync(env, t, options, cancellationToken),
                cancellationToken);
            telemetry.Complete();
            return messageId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            telemetry.Fail(ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
        string topic,
        IEnumerable<TMessage> messages,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RejectCallerAssignedBatchMessageId(options);

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Use a single channel for the entire batch for efficiency
        var channelPool = ResolvePublishChannelPool(options);
        var channel = await channelPool.BorrowAsync(cancellationToken);
        try
        {
            if (_options.AutoCreateTopology)
            {
                await EnsureTopologyAsync(channel, topic, cancellationToken);
            }

            // Publisher confirms are enabled at channel creation time via ChannelPool


            var messageIds = new List<string>(messageList.Count);

            // Confirm-enabled BasicPublishAsync waits for broker acknowledgement. One deadline
            // bounds the complete batch while preserving the caller's cancellation token.
            if (ShouldWaitForConfirmation(_options, options))
            {
                 await WithConfirmsAsync(async confirmCancellationToken =>
                 {

                    foreach (var message in messageList)
                    {
                        using var publishTelemetry = _telemetry.StartPublish(CreateEnvelope(message, options), "rabbitmq", topic);
                        var envelope = publishTelemetry.Envelope;
                        messageIds.Add(envelope.MessageId);

                        var body = _serializer.Serialize(envelope);
                        var properties = new BasicProperties
                        {
                            MessageId = envelope.MessageId,
                            CorrelationId = envelope.CorrelationId,
                            Timestamp = new AmqpTimestamp(envelope.Timestamp.ToUnixTimeSeconds()),
                            ContentType = _serializer.ContentType,
                            DeliveryMode = envelope.Persistent ? DeliveryModes.Persistent : DeliveryModes.Transient,
                            Priority = envelope.Priority,
                            Headers = new Dictionary<string, object?>()
                        };

                        if (envelope.TimeToLive.HasValue)
                        {
                            properties.Expiration = ((int)envelope.TimeToLive.Value.TotalMilliseconds).ToString();
                        }

                        if (envelope.Headers != null)
                        {
                            foreach (var header in envelope.Headers)
                            {
                                properties.Headers[header.Key] = header.Value;
                            }
                        }

                        await channel.BasicPublishAsync(
                            exchange: topic,
                            routingKey: options?.RoutingKey ?? topic,
                            mandatory: false,
                            basicProperties: properties,
                            body: body,
                            cancellationToken: confirmCancellationToken);
                        publishTelemetry.Complete();
                    }
                 }, _options.PublisherConfirmTimeout, cancellationToken);
                _logger.LogDebug(ConfirmedBatchPublished, "Batch published {Count} messages with confirmation", messageIds.Count);
            }
            else
            {
                foreach (var message in messageList)
                {
                    using var publishTelemetry = _telemetry.StartPublish(CreateEnvelope(message, options), "rabbitmq", topic);
                    var envelope = publishTelemetry.Envelope;
                    messageIds.Add(envelope.MessageId);

                    var body = _serializer.Serialize(envelope);
                    var properties = new BasicProperties
                    {
                        MessageId = envelope.MessageId,
                        CorrelationId = envelope.CorrelationId,
                        Timestamp = new AmqpTimestamp(envelope.Timestamp.ToUnixTimeSeconds()),
                        ContentType = _serializer.ContentType,
                        DeliveryMode = envelope.Persistent ? DeliveryModes.Persistent : DeliveryModes.Transient,
                        Priority = envelope.Priority,
                        Headers = new Dictionary<string, object?>()
                    };

                    if (envelope.TimeToLive.HasValue)
                    {
                        properties.Expiration = ((int)envelope.TimeToLive.Value.TotalMilliseconds).ToString();
                    }

                    if (envelope.Headers != null)
                    {
                        foreach (var header in envelope.Headers)
                        {
                            properties.Headers[header.Key] = header.Value;
                        }
                    }

                    await channel.BasicPublishAsync(
                        exchange: topic,
                        routingKey: options?.RoutingKey ?? topic,
                        mandatory: false,
                        basicProperties: properties,
                        body: body,
                        cancellationToken: cancellationToken);
                    publishTelemetry.Complete();
                }
                _logger.LogDebug(UnconfirmedBatchPublished, "Batch published {Count} messages without confirmation", messageIds.Count);
            }


            return messageIds;
        }
        finally
        {
            channelPool.Return(channel);
        }
    }

    public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic) where TMessage : class
    {
        return new RabbitMQPublisherBuilder<TMessage>(this, topic);
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateDeliveryPolicy(options);

        IChannel? channel = null;
        SemaphoreSlim? concurrencySemaphore = null;
        try
        {
            // Create a dedicated channel for this consumer (not shared - consumers need dedicated channels)
            if (_connection == null || !_connection.IsOpen)
            {
                throw new InvalidOperationException("RabbitMQ connection is not available. The message bus may have been disposed.");
            }

            channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            if (_options.AutoCreateTopology)
            {
                await EnsureTopologyAsync(channel, topic, cancellationToken);
            }

            var queueName = await DeclareQueueAsync(channel, topic, options, cancellationToken);
            await channel.BasicQosAsync(
                0,
                options?.PrefetchCount ?? _options.PrefetchCount,
                false,
                cancellationToken);

            // Create semaphore to enforce MaxConcurrency
            var maxConcurrency = options?.MaxConcurrency ?? 1;
            concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                // Enforce concurrency limit - only allow MaxConcurrency messages to be processed simultaneously
                await concurrencySemaphore.WaitAsync(cancellationToken);
                try
                {
                    await HandleMessageAsync(channel, ea, topic, queueName, handler, options, cancellationToken);
                }
                finally
                {
                    concurrencySemaphore.Release();
                }
            };

            var autoAck = options?.AutoAck ?? false;
            var consumerTag = await channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: autoAck,
                consumer: consumer,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                SubscriptionStarted,
                "Started consuming messages from topic '{Topic}' on queue '{Queue}' with consumer tag '{ConsumerTag}' (MaxConcurrency: {MaxConcurrency})",
                topic, queueName, consumerTag, maxConcurrency);

            return new RabbitMQSubscription(consumerTag, topic, queueName, channel, consumer, autoAck, concurrencySemaphore, _logger);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            concurrencySemaphore?.Dispose();
            channel?.Dispose();
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch
        {
            concurrencySemaphore?.Dispose();
            channel?.Dispose();
            throw;
        }
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        IEnumerable<string> topics,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var topicList = topics?.ToList() ?? throw new ArgumentNullException(nameof(topics));

        if (topicList.Count == 0)
        {
            throw new ArgumentException("Must specify at least one topic", nameof(topics));
        }

        // Create individual subscription for each topic
        var subscriptions = new List<IMessageSubscription>(topicList.Count);

        try
        {
            foreach (var topic in topicList)
            {
                var subscription = await SubscribeAsync(topic, handler, options, cancellationToken);
                subscriptions.Add(subscription);
            }

            // Return composite subscription that manages all individual subscriptions
            return new CompositeSubscription(subscriptions);
        }
        catch
        {
            // If any subscription fails, clean up all created subscriptions
            foreach (var sub in subscriptions)
            {
                try
                {
                    await sub.DisposeAsync();
                }
                catch
                {
                    // Swallow disposal errors during cleanup
                }
            }
            throw;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return _connection != null && _connection.IsOpen;
    }

    private MessageEnvelope<TMessage> CreateEnvelope<TMessage>(TMessage message, PublishOptions? options) where TMessage : class
    {
        return new MessageEnvelope<TMessage>
        {
            MessageId = ResolveMessageId(options),
            CorrelationId = options?.CorrelationId,
            CausationId = options?.CausationId,
            Source = options?.Source,
            MessageType = typeof(TMessage).FullName ?? typeof(TMessage).Name,
            Timestamp = DateTimeOffset.UtcNow,
            Headers = options?.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString() ?? string.Empty) ?? new Dictionary<string, string>(),  // Defensive copy to prevent external modification
            Payload = message,
            Priority = options?.Priority ?? 0,
            TimeToLive = options?.TimeToLive,
            DeliveryDelay = options?.DeliveryDelay,
            Persistent = options?.Persistent ?? true
        };
    }

    private async Task<string> PublishInternalAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        PublishOptions? options,
        CancellationToken cancellationToken) where TMessage : class
    {
        // Borrow a channel from the pool for thread-safe publishing
        var channelPool = ResolvePublishChannelPool(options);
        var channel = await channelPool.BorrowAsync(cancellationToken);
        try
        {
            if (_options.AutoCreateTopology)
            {
                await EnsureTopologyAsync(channel, topic, cancellationToken);
            }

            // Publisher confirms are enabled at channel creation time via ChannelPool


            var body = _serializer.Serialize(envelope);
            var properties = new BasicProperties
            {
                MessageId = envelope.MessageId,
                CorrelationId = envelope.CorrelationId,
                Timestamp = new AmqpTimestamp(envelope.Timestamp.ToUnixTimeSeconds()),
                ContentType = _serializer.ContentType,
                DeliveryMode = envelope.Persistent ? DeliveryModes.Persistent : DeliveryModes.Transient,
                Priority = envelope.Priority,
                Headers = new Dictionary<string, object?>()
            };

            if (envelope.DeliveryDelay.HasValue && envelope.DeliveryDelay.Value > TimeSpan.Zero)
            {
                properties.Headers["x-delay"] = (int)envelope.DeliveryDelay.Value.TotalMilliseconds;
            }

            if (envelope.TimeToLive.HasValue)
            {
                properties.Expiration = ((int)envelope.TimeToLive.Value.TotalMilliseconds).ToString();
            }

            if (envelope.Headers != null)
            {
                foreach (var header in envelope.Headers)
                {
                    properties.Headers[header.Key] = header.Value;
                }
            }

            if (ShouldWaitForConfirmation(_options, options))
            {
                await WithConfirmsAsync(async confirmCancellationToken =>
                {
                    await channel.BasicPublishAsync(
                        exchange: topic,
                        routingKey: options?.RoutingKey ?? topic,
                        mandatory: false,
                        basicProperties: properties,
                        body: body,
                        cancellationToken: confirmCancellationToken);
                }, _options.PublisherConfirmTimeout, cancellationToken);
            }
            else
            {
                await channel.BasicPublishAsync(
                    exchange: topic,
                    routingKey: options?.RoutingKey ?? topic,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: cancellationToken);
            }


            return envelope.MessageId;
        }
        finally
        {
            // Always return the channel to the pool
            channelPool.Return(channel);
        }
    }

    private async Task HandleMessageAsync<TMessage>(
        IChannel channel,
        BasicDeliverEventArgs ea,
        string topic,
        string queueName,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options,
        CancellationToken cancellationToken) where TMessage : class
    {
        // Compute delivery count up front so the catch block can use it for the same poison-message
        // ceiling as the explicit Retry path. Source order is x-death header (set by DLX wiring) →
        // ea.Redelivered → 1.
        var messageId = GetBrokerMessageId(ea);
        DeliveryAttemptKey? deliveryKey = messageId is null
            ? null
            : new DeliveryAttemptKey(queueName, messageId);
        var deliveryCount = deliveryKey is null
            ? CalculateDeliveryCount(ea)
            : TrackDeliveryCount(deliveryKey.Value);
        var messageDecoded = false;
        using var telemetry = _telemetry.StartDelivery("rabbitmq", topic, options?.ConsumerGroup);

        try
        {
            var envelope = _serializer.Deserialize<MessageEnvelope<TMessage>>(ea.Body.ToArray());
            messageDecoded = true;
            if (messageId is null && string.IsNullOrWhiteSpace(envelope.MessageId))
            {
                telemetry.AttachEnvelope(envelope, deliveryCount);
                _logger.LogCritical(
                    DeliveryWithoutIdentityRejected,
                    "Message on topic '{Topic}' rejected before handler invocation because neither the AMQP delivery nor the decoded envelope provides a stable MessageId.",
                    topic);
                var rejected = await HandleMessageResultAsync(
                    channel,
                    ea.DeliveryTag,
                    MessageResult.Reject("Delivery has no stable message identifier."),
                    options,
                    deliveryCount,
                    cancellationToken);
                telemetry.Complete(rejected.Status);
                return;
            }

            if (messageId is null)
            {
                messageId = envelope.MessageId;
                deliveryKey = new DeliveryAttemptKey(queueName, messageId);
                deliveryCount = TrackDeliveryCount(deliveryKey.Value);
            }
            telemetry.AttachEnvelope(envelope, deliveryCount);

            var context = new MessageContext<TMessage>
            {
                Envelope = envelope,
                Topic = topic,
                ConsumerGroup = options?.ConsumerGroup,
                DeliveryCount = deliveryCount,
                CancellationToken = cancellationToken
            };

            var result = await _pipeline.ExecuteConsumeAsync(context, handler, cancellationToken);

            var appliedResult = await HandleMessageResultAsync(channel, ea.DeliveryTag, result, options, deliveryCount, cancellationToken);
            telemetry.Complete(appliedResult.Status);
            if (appliedResult.Terminal)
            {
                ForgetDelivery(deliveryKey);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RollBackDeliveryAttempt(deliveryKey);
            telemetry.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            if (options?.AutoAck ?? false)
            {
                _logger.LogError(
                    AutoAcknowledgedDeliveryFailed,
                    ex,
                    "Error handling message from topic '{Topic}' (DeliveryCount={DeliveryCount}); auto-ack is enabled, message already ack'd by broker.",
                    topic,
                    deliveryCount);
                telemetry.Unhandled(ex);
                ForgetDelivery(deliveryKey);
                return;
            }

            // Default behaviour: an unhandled exception means a poison message — reject without requeue
            // so the broker routes to the configured DLX (or drops it) instead of looping the same
            // message back at the same broken consumer at thousands of deliveries per second.
            //
            // Callers can opt into requeue for known-transient exceptions with
            // SubscribeOptions.RequeueOnException = true; the finite effective cap still applies.
            // Decode failures are permanent input failures. Requeueing the same malformed bytes cannot
            // succeed. A decoded delivery without either broker or envelope identity also cannot be
            // counted reliably across immediate RabbitMQ requeues. Reject either case once instead of
            // violating the configured finite-delivery ceiling.
            var shouldRequeue = messageDecoded &&
                deliveryKey is not null &&
                (options?.RequeueOnException ?? false) &&
                !ExceedsMaxDeliveryCount(deliveryCount, options);
            var exceptionResultApplied = false;
            try
            {
                if (shouldRequeue)
                {
                    _logger.LogError(
                        DeliveryRetrying,
                        ex,
                        "Error handling message from topic '{Topic}' (DeliveryCount={DeliveryCount}); requeueing for retry.",
                        topic,
                        deliveryCount);
                    await channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);
                    exceptionResultApplied = true;
                }
                else
                {
                    _logger.LogCritical(
                        PoisonDeliveryRejected,
                        ex,
                        "Poison message on topic '{Topic}' rejected (DeliveryCount={DeliveryCount}). MessageId={MessageId}.",
                        topic, deliveryCount,
                        TryExtractMessageId(ea));
                    await channel.BasicRejectAsync(ea.DeliveryTag, false, cancellationToken);
                    exceptionResultApplied = true;
                    ForgetDelivery(deliveryKey);
                }
            }
            finally
            {
                if (!exceptionResultApplied && cancellationToken.IsCancellationRequested)
                {
                    RollBackDeliveryAttempt(deliveryKey);
                    telemetry.Cancel();
                }
                else
                {
                    telemetry.Unhandled(ex);
                }
            }
        }
    }

    private static string TryExtractMessageId(BasicDeliverEventArgs ea)
    {
        // Best-effort: the broker-level MessageId property is what AMQP sets; the envelope-level
        // MessageId lives inside the body. We log both ours-if-broker-set and 'unknown' fallback.
        var msgId = ea.BasicProperties?.MessageId;
        return string.IsNullOrEmpty(msgId) ? "<unknown>" : msgId;
    }

    private static string? GetBrokerMessageId(BasicDeliverEventArgs ea)
    {
        var messageId = ea.BasicProperties?.MessageId;
        return string.IsNullOrWhiteSpace(messageId) ? null : messageId;
    }

    private async Task<(MessageStatus Status, bool Terminal)> HandleMessageResultAsync(
        IChannel channel,
        ulong deliveryTag,
        MessageResult result,
        SubscribeOptions? options,
        int deliveryCount,
        CancellationToken cancellationToken)
    {
        if (options?.AutoAck ?? false)
        {
            return (result.Status, true); // Auto-ack is enabled, nothing to do
        }

        switch (result.Status)
        {
            case MessageStatus.Success:
                await channel.BasicAckAsync(deliveryTag, false, cancellationToken);
                return (MessageStatus.Success, true);
            case MessageStatus.Retry:
                if (ExceedsMaxDeliveryCount(deliveryCount, options))
                {
                    // Cap reached — escalate to Reject so the broker stops requeuing.
                    _logger.LogWarning(
                        DeliveryLimitReached,
                        "Message exceeded MaxDeliveryCount={MaxDeliveryCount} on Retry; rejecting (DeliveryCount={DeliveryCount}). Reason: {Reason}",
                        ResolveMaxDeliveryCount(options), deliveryCount, result.Reason);
                    await channel.BasicRejectAsync(deliveryTag, false, cancellationToken);
                    return (MessageStatus.Reject, true);
                }
                else
                {
                    await Task.Delay(GetRetryDelay(deliveryCount, options), cancellationToken);
                    await channel.BasicNackAsync(deliveryTag, false, true, cancellationToken); // Requeue
                    return (MessageStatus.Retry, false);
                }
            case MessageStatus.Reject:
                await channel.BasicRejectAsync(deliveryTag, false, cancellationToken); // Don't requeue, send to DLQ
                return (MessageStatus.Reject, true);
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown message result status.");
        }
    }

    private static bool ExceedsMaxDeliveryCount(int deliveryCount, SubscribeOptions? options)
    {
        return deliveryCount >= ResolveMaxDeliveryCount(options);
    }

    private static int ResolveMaxDeliveryCount(SubscribeOptions? options)
    {
        return options?.MaxDeliveryCount
            ?? checked((options?.RetryPolicy?.MaxRetryAttempts ?? new RetryPolicy().MaxRetryAttempts) + 1);
    }

    private static TimeSpan GetRetryDelay(int deliveryCount, SubscribeOptions? options)
    {
        var policy = options?.RetryPolicy ?? new RetryPolicy();
        var multiplier = policy.UseExponentialBackoff
            ? Math.Pow(policy.BackoffMultiplier, Math.Max(0, deliveryCount - 1))
            : 1d;
        var delay = TimeSpan.FromTicks((long)Math.Min(
            policy.InitialDelay.Ticks * multiplier,
            policy.MaxDelay.Ticks));
        if (policy.UseJitter)
        {
            const double MinimumFactor = 0.8d;
            const double JitterRange = 0.4d;
            var randomFraction = RandomNumberGenerator.GetInt32(0, 10_001) / 10_000d;
            var jitteredTicks = delay.Ticks * (MinimumFactor + (randomFraction * JitterRange));
            delay = TimeSpan.FromTicks((long)Math.Min(jitteredTicks, policy.MaxDelay.Ticks));
        }

        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
    }

    private static void ValidateDeliveryPolicy(SubscribeOptions? options)
    {
        var policy = options?.RetryPolicy ?? new RetryPolicy();
        if (policy.MaxRetryAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryPolicy.MaxRetryAttempts must be at least one.");
        }

        if (policy.InitialDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryPolicy.InitialDelay must be greater than zero.");
        }

        if (policy.MaxDelay < policy.InitialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryPolicy.MaxDelay must be at least InitialDelay.");
        }

        if (policy.UseExponentialBackoff && policy.BackoffMultiplier < 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryPolicy.BackoffMultiplier must be at least one.");
        }

        if (ResolveMaxDeliveryCount(options) < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDeliveryCount must be at least one.");
        }
    }

    private int TrackDeliveryCount(DeliveryAttemptKey deliveryKey)
    {
        return _deliveryAttempts.AddOrUpdate(
            deliveryKey,
            1,
            (_, previous) => checked(previous + 1));
    }

    private void ForgetDelivery(DeliveryAttemptKey? deliveryKey)
    {
        if (deliveryKey is not null)
        {
            _deliveryAttempts.TryRemove(deliveryKey.Value, out _);
        }
    }

    private void RollBackDeliveryAttempt(DeliveryAttemptKey? deliveryKey)
    {
        if (deliveryKey is null || !_deliveryAttempts.TryGetValue(deliveryKey.Value, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            _deliveryAttempts.TryRemove(deliveryKey.Value, out _);
        }
        else
        {
            _deliveryAttempts.TryUpdate(deliveryKey.Value, count - 1, count);
        }
    }

    private readonly record struct DeliveryAttemptKey(string QueueName, string MessageId);

    private static string ResolveMessageId(PublishOptions? options)
    {
        if (options?.MessageId is null)
        {
            return Guid.NewGuid().ToString();
        }

        if (string.IsNullOrWhiteSpace(options.MessageId))
        {
            throw new ArgumentException("MessageId cannot be empty or whitespace.", nameof(options));
        }

        return options.MessageId;
    }

    private static void RejectCallerAssignedBatchMessageId(PublishOptions? options)
    {
        if (options?.MessageId is not null)
        {
            throw new ArgumentException(
                "PublishOptions.MessageId cannot be used for batch publishing because each message needs a distinct identifier.",
                nameof(options));
        }
    }

    private async Task EnsureTopologyAsync(
        IChannel channel,
        string topic,
        CancellationToken cancellationToken)
    {
        var args = new Dictionary<string, object?>();
        
        if (_options.DefaultExchangeType == "x-delayed-message")
        {
            args["x-delayed-type"] = "fanout";
        }

        await channel.ExchangeDeclareAsync(
            exchange: topic,
            type: _options.DefaultExchangeType,
            durable: true,
            autoDelete: false,
            arguments: args,
            cancellationToken: cancellationToken);
    }

    private async Task<string> DeclareQueueAsync(
        IChannel channel,
        string topic,
        SubscribeOptions? options,
        CancellationToken cancellationToken)
    {
        var queueName = options?.ConsumerGroup ?? $"{topic}.queue";

        var args = new Dictionary<string, object?>();

        // Default to wiring DLX. Caller can opt out by passing
        // SubscribeOptions { DeadLetterQueue = new() { Enabled = false } }.
        var dlqEnabled = options?.DeadLetterQueue?.Enabled ?? true;
        if (dlqEnabled)
        {
            var dlqOptions = options?.DeadLetterQueue;
            var dlxExchange = dlqOptions?.ExchangeName ?? $"{topic}.dlx";
            var dlqName = dlqOptions?.QueueName ?? $"{topic}.dlq";

            try
            {
                // Ensure DLX exchange exists
                await channel.ExchangeDeclareAsync(
                    exchange: dlxExchange,
                    type: ExchangeType.Fanout, // Use fanout for DLX to simplify routing
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken);

                // Ensure DLQ exists
                await channel.QueueDeclareAsync(
                    queue: dlqName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: cancellationToken);

                // Bind DLQ to DLX
                await channel.QueueBindAsync(
                    queue: dlqName,
                    exchange: dlxExchange,
                    routingKey: string.Empty,
                    cancellationToken: cancellationToken);

                args["x-dead-letter-exchange"] = dlxExchange;
                args["x-dead-letter-routing-key"] = string.Empty; // Fanout ignores routing key

                _logger.LogDebug(
                    DeadLetterTopologyCreated,
                    "DLX wired for topic '{Topic}': exchange='{DlxExchange}', queue='{DlqName}'",
                    topic, dlxExchange, dlqName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // DLX wiring is best-effort. If broker permissions prohibit it or another consumer
                // already declared the queue with different x-dead-letter-* args, we log a warning
                // and proceed without DLX. Poison messages on this queue will then be dropped after
                // BasicReject; the structured Critical log on the rejection path remains the audit
                // trail in that case.
                _logger.LogWarning(
                    DeadLetterTopologyCreationFailed,
                    ex,
                    "Could not wire DLX for topic '{Topic}'. Poison messages on this consumer will be dropped after rejection (see Critical logs for message metadata). To remediate: ensure the user has DECLARE permission on '{DlxExchange}' and '{DlqName}', or delete a pre-existing queue declared with conflicting args.",
                    topic, dlxExchange, dlqName);
            }
        }

        if (options?.MaxPriority.HasValue == true)
        {
            args["x-max-priority"] = options.MaxPriority.Value;
        }

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: args,
            cancellationToken: cancellationToken);

        // Bind queue to exchange with routing patterns
        // Support multiple routing patterns or a single pattern
        var routingPatterns = options?.RoutingPatterns ??
                             (options?.RoutingPattern != null
                                 ? new List<string> { options.RoutingPattern }
                                 : new List<string> { "#" }); // Default to all messages

        foreach (var routingPattern in routingPatterns)
        {
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: topic,
                routingKey: routingPattern,
                cancellationToken: cancellationToken);
        }

        return queueName;
    }

    private int CalculateDeliveryCount(BasicDeliverEventArgs ea)
    {
        // Start with 1 (first delivery attempt)
        var count = 1;

        // If redelivered, it's at least the second attempt
        if (ea.Redelivered)
        {
            count = 2;
        }

        // Check x-death header for accurate count (from DLX retries)
        if (ea.BasicProperties.Headers != null &&
            ea.BasicProperties.Headers.TryGetValue("x-death", out var xDeathObj) &&
            xDeathObj is List<object> xDeathList &&
            xDeathList.Count > 0 &&
            xDeathList[0] is Dictionary<string, object> xDeath)
        {
            // x-death contains count field showing how many times message was dead-lettered
            if (xDeath.TryGetValue("count", out var countObj))
            {
                if (countObj is long longCount)
                {
                    count = (int)longCount + 1; // +1 for current attempt
                }
                else if (countObj is int intCount)
                {
                    count = intCount + 1;
                }
            }
        }

        return count;
    }

    internal static async Task WithConfirmsAsync(
        Func<CancellationToken, Task> publishAction,
        TimeSpan publisherConfirmTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publishAction);

        using var confirmationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        confirmationCancellation.CancelAfter(publisherConfirmTimeout);

        try
        {
            await publishAction(confirmationCancellation.Token);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (OperationCanceledException ex) when (
            !cancellationToken.IsCancellationRequested &&
            confirmationCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"RabbitMQ publisher confirmation did not complete within {publisherConfirmTimeout}.",
                ex);
        }
    }

    internal static bool ShouldWaitForConfirmation(
        RabbitMQOptions providerOptions,
        PublishOptions? publishOptions)
    {
        ArgumentNullException.ThrowIfNull(providerOptions);
        return providerOptions.UsePublisherConfirms &&
            (publishOptions?.WaitForConfirmation ?? true);
    }

    private ChannelPool ResolvePublishChannelPool(PublishOptions? options)
    {
        return ShouldWaitForConfirmation(_options, options)
            ? _defaultChannelPool
            : _unconfirmedChannelPool ?? _defaultChannelPool;
    }

    public async ValueTask DisposeAsync()

    {
        if (_disposed) return;

        // Set disposed flag FIRST to prevent new operations
        _disposed = true;

        // Dispose the channel pool
        await _defaultChannelPool.DisposeAsync();
        if (_unconfirmedChannelPool is not null)
        {
            await _unconfirmedChannelPool.DisposeAsync();
        }

        if (_connection != null)
        {
            try
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ConnectionCloseFailed, ex, "Error closing connection");
            }
        }

        _logger.LogInformation(QueueDisposed, "RabbitMQ message bus '{Name}' disposed", _name);
    }
}
