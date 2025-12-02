using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Middleware;

using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace Ruya.Services.MessageQueue.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of IMessageQueue
/// </summary>
internal sealed class RabbitMQMessageQueue : IMessageQueue
{
    private readonly string _name;
    private readonly RabbitMQOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly ILogger _logger;
    private readonly ChannelPool _channelPool;
    private volatile IConnection? _connection;
    private volatile bool _disposed;

    public RabbitMQMessageQueue(
        string name,
        IConnection connection,
        RabbitMQOptions options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _channelPool = new ChannelPool(_connection, _options.ChannelPoolSize, _options.UsePublisherConfirms, _logger);


        _logger.LogInformation("RabbitMQ message bus '{Name}' initialized", _name);
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

        var envelope = CreateEnvelope(message, options);

        return await _pipeline.ExecutePublishAsync(
            envelope,
            topic,
            async (env, t) => await PublishInternalAsync(env, t, options, cancellationToken),
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
        string topic,
        IEnumerable<TMessage> messages,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Use a single channel for the entire batch for efficiency
        var channel = await _channelPool.BorrowAsync(cancellationToken);
        try
        {
            if (_options.AutoCreateTopology)
            {
                await EnsureTopologyAsync(channel, topic);
            }

            // Publisher confirms are enabled at channel creation time via ChannelPool


            var messageIds = new List<string>(messageList.Count);

            // Wait for confirmation ONCE for all messages (true batch)
            if (_options.UsePublisherConfirms)
            {
                 await WithConfirmsAsync(channel, async () =>
                 {

                    foreach (var message in messageList)
                    {
                        var envelope = CreateEnvelope(message, options);
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
                    }
                 }, cancellationToken);
                _logger.LogDebug("Batch published {Count} messages with confirmation", messageIds.Count);
            }
            else
            {
                foreach (var message in messageList)
                {
                    var envelope = CreateEnvelope(message, options);
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
                }
                _logger.LogDebug("Batch published {Count} messages without confirmation", messageIds.Count);
            }


            return messageIds;
        }
        finally
        {
            _channelPool.Return(channel);
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

        // Create a dedicated channel for this consumer (not shared - consumers need dedicated channels)
        if (_connection == null || !_connection.IsOpen)
        {
            throw new InvalidOperationException("RabbitMQ connection is not available. The message bus may have been disposed.");
        }

        var channel = await _connection.CreateChannelAsync();

        if (_options.AutoCreateTopology)
        {
            await EnsureTopologyAsync(channel, topic);
        }

        var queueName = await DeclareQueueAsync(channel, topic, options);
        await channel.BasicQosAsync(0, options?.PrefetchCount ?? _options.PrefetchCount, false);

        // Create semaphore to enforce MaxConcurrency
        var maxConcurrency = options?.MaxConcurrency ?? 1;
        var concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            // Enforce concurrency limit - only allow MaxConcurrency messages to be processed simultaneously
            await concurrencySemaphore.WaitAsync(cancellationToken);
            try
            {
                await HandleMessageAsync(channel, ea, topic, handler, options, cancellationToken);
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
            consumer: consumer);

        _logger.LogInformation(
            "Started consuming messages from topic '{Topic}' on queue '{Queue}' with consumer tag '{ConsumerTag}' (MaxConcurrency: {MaxConcurrency})",
            topic, queueName, consumerTag, maxConcurrency);

        return new RabbitMQSubscription(consumerTag, topic, queueName, channel, consumer, autoAck, concurrencySemaphore, _logger);
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
            MessageId = Guid.NewGuid().ToString(),
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
        var channel = await _channelPool.BorrowAsync(cancellationToken);
        try
        {
            if (_options.AutoCreateTopology)
            {
                await EnsureTopologyAsync(channel, topic);
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

            if (_options.UsePublisherConfirms)
            {
                await WithConfirmsAsync(channel, async () =>
                {
                    await channel.BasicPublishAsync(
                        exchange: topic,
                        routingKey: options?.RoutingKey ?? topic,
                        mandatory: false,
                        basicProperties: properties,
                        body: body,
                        cancellationToken: cancellationToken);
                }, cancellationToken);
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
            _channelPool.Return(channel);
        }
    }

    private async Task HandleMessageAsync<TMessage>(
        IChannel channel,
        BasicDeliverEventArgs ea,
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options,
        CancellationToken cancellationToken) where TMessage : class
    {
        try
        {
            var envelope = _serializer.Deserialize<MessageEnvelope<TMessage>>(ea.Body.ToArray());

            // Calculate delivery count:
            // 1. Use ea.Redelivered to detect if message was redelivered (at least attempt 2)
            // 2. Check x-death header from DLX for accurate retry count
            var deliveryCount = CalculateDeliveryCount(ea);

            var context = new MessageContext<TMessage>
            {
                Envelope = envelope,
                Topic = topic,
                ConsumerGroup = options?.ConsumerGroup,
                DeliveryCount = deliveryCount,
                CancellationToken = cancellationToken
            };

            var result = await _pipeline.ExecuteConsumeAsync(context, handler, cancellationToken);

            await HandleMessageResultAsync(channel, ea.DeliveryTag, result, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from topic '{Topic}'", topic);

            if (!(options?.AutoAck ?? false))
            {
                await channel.BasicNackAsync(ea.DeliveryTag, false, true);
            }
        }
    }

    private async Task HandleMessageResultAsync(
        IChannel channel,
        ulong deliveryTag,
        MessageResult result,
        SubscribeOptions? options)
    {
        if (options?.AutoAck ?? false)
        {
            return; // Auto-ack is enabled, nothing to do
        }

        switch (result.Status)
        {
            case MessageStatus.Success:
                await channel.BasicAckAsync(deliveryTag, false);
                break;
            case MessageStatus.Retry:
                await channel.BasicNackAsync(deliveryTag, false, true); // Requeue
                break;
            case MessageStatus.Reject:
                await channel.BasicRejectAsync(deliveryTag, false); // Don't requeue, send to DLQ
                break;
        }
    }

    private async Task EnsureTopologyAsync(IChannel channel, string topic)
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
            arguments: args);
    }

    private async Task<string> DeclareQueueAsync(IChannel channel, string topic, SubscribeOptions? options)
    {
        var queueName = options?.ConsumerGroup ?? $"{topic}.queue";

        var args = new Dictionary<string, object?>();

        if (options?.DeadLetterQueue?.Enabled ?? false)
        {
            var dlxExchange = $"{topic}.dlx";
            var dlqName = options.DeadLetterQueue.QueueName;

            // Ensure DLX exchange exists
            await channel.ExchangeDeclareAsync(
                exchange: dlxExchange,
                type: ExchangeType.Fanout, // Use fanout for DLX to simplify routing
                durable: true,
                autoDelete: false);

            // Ensure DLQ exists
            await channel.QueueDeclareAsync(
                queue: dlqName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            // Bind DLQ to DLX
            await channel.QueueBindAsync(
                queue: dlqName,
                exchange: dlxExchange,
                routingKey: string.Empty);

            args["x-dead-letter-exchange"] = dlxExchange;
            args["x-dead-letter-routing-key"] = string.Empty; // Fanout ignores routing key
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
            arguments: args);

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
                routingKey: routingPattern);
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

    private async Task WithConfirmsAsync(IChannel channel, Func<Task> publishAction, CancellationToken cancellationToken)
    {
        await publishAction();
        // await channel.WaitForConfirmsAsync(cancellationToken); // Not available in v7, assuming BasicPublishAsync handles it or we accept fire-and-forget for now
    }

    public async ValueTask DisposeAsync()

    {
        if (_disposed) return;

        // Set disposed flag FIRST to prevent new operations
        _disposed = true;

        // Dispose the channel pool
        await _channelPool.DisposeAsync();

        if (_connection != null)
        {
            try
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing connection");
            }
        }

        _logger.LogInformation("RabbitMQ message bus '{Name}' disposed", _name);
    }
}
