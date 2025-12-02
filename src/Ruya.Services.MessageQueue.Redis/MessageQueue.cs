using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Utilities;
using System.Collections.Generic;
using System;
using System.Threading;
using Ruya.Services.MessageQueue.Serialization;
using System.Threading.Tasks;
using System.Linq;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Redis implementation of IMessageQueue
/// Note: This is a reference implementation showing the structure.
/// Full implementation would include Pub/Sub and Streams support.
/// </summary>
internal sealed class RedisMessageQueue : IMessageQueue
{
    private readonly string _name;
    private readonly RedisOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
    private volatile IConnectionMultiplexer? _connection;
    private volatile bool _disposed;

    public RedisMessageQueue(
        string name,
        IOptions<RedisOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => _name;
    public string Provider => "Redis";

    private async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null && _connection.IsConnected)
        {
            return _connection;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_connection != null && _connection.IsConnected)
            {
                return _connection;
            }

            // Dispose old connection if it exists and is disconnected to prevent connection leak
            if (_connection != null)
            {
                _logger.LogDebug("Disposing disconnected Redis connection for bus '{Name}'", _name);
                try
                {
                    await _connection.CloseAsync();
                    _connection.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing old Redis connection for bus '{Name}'", _name);
                }
                _connection = null;  // Clear reference before creating new connection
            }

            var configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
            configOptions.ConnectTimeout = (int)_options.ConnectionTimeout.TotalMilliseconds;
            configOptions.SyncTimeout = (int)_options.SyncTimeout.TotalMilliseconds;
            configOptions.AbortOnConnectFail = _options.AbortOnConnectFail;
            configOptions.ConnectRetry = _options.RetryCount;

            _connection = await ConnectionMultiplexer.ConnectAsync(configOptions);
            _logger.LogInformation("Redis connection established for bus '{Name}'", _name);

            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

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
            async (env, t) => await PublishInternalAsync(env, t, cancellationToken),
            cancellationToken);
    }

    private async Task<string> PublishInternalAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        CancellationToken cancellationToken) where TMessage : class
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var db = connection.GetDatabase(_options.Database);

        // Use routing key from headers if specified, otherwise use topic
        var routingKey = envelope.Headers?.TryGetValue("X-RoutingKey", out var key) == true ? key : topic;
        var channel = $"{_options.KeyPrefix}{routingKey}";

        if (_options.UsePubSub)
        {
            // Pub/Sub implementation
            var payload = _serializer.Serialize(envelope);
            await db.PublishAsync(RedisChannel.Literal(channel), payload);

            _logger.LogDebug("Published message {MessageId} to Redis channel {Channel}", envelope.MessageId, channel);
        }
        else if (_options.UseStreams)
        {
            // Streams implementation
            var fields = new[]
            {
                new NameValueEntry("messageId", envelope.MessageId),
                new NameValueEntry("payload", _serializer.Serialize(envelope))
            };

            var messageId = await db.StreamAddAsync(channel, fields);

            _logger.LogDebug("Added message to Redis stream {Stream}: {MessageId}", channel, messageId);

            // Trim stream if max length is specified
            if (_options.StreamOptions?.MaxLength.HasValue ?? false)
            {
                await db.StreamTrimAsync(channel, _options.StreamOptions.MaxLength.Value,
                    useApproximateMaxLength: _options.StreamOptions.UseApproximateMaxLength);
            }
        }

        return envelope.MessageId;
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

        var connection = await GetConnectionAsync(cancellationToken);
        var db = connection.GetDatabase(_options.Database);
        var channel = $"{_options.KeyPrefix}{topic}";
        var messageIds = new List<string>(messageList.Count);

        // Use Redis batch/pipeline for efficient bulk operations
        var batch = db.CreateBatch();
        var batchTasks = new List<Task>();

        foreach (var message in messageList)
        {
            var envelope = CreateEnvelope(message, options);
            messageIds.Add(envelope.MessageId);

            var payload = _serializer.Serialize(envelope);

            if (_options.UsePubSub)
            {
                // Batch Pub/Sub publishes
                var publishTask = batch.PublishAsync(RedisChannel.Literal(channel), payload);
                batchTasks.Add(publishTask);
            }
            else if (_options.UseStreams)
            {
                // Batch Stream additions
                var fields = new[]
                {
                    new NameValueEntry("messageId", envelope.MessageId),
                    new NameValueEntry("payload", payload)
                };

                var streamTask = batch.StreamAddAsync(channel, fields);
                batchTasks.Add(streamTask);
            }
        }

        // Execute the entire batch as a pipeline (single round-trip)
        batch.Execute();
        await Task.WhenAll(batchTasks);

        _logger.LogDebug("Batch published {Count} messages to Redis using pipeline", messageIds.Count);

        // Trim stream if needed (after batch completes)
        if (_options.UseStreams && (_options.StreamOptions?.MaxLength.HasValue ?? false))
        {
            await db.StreamTrimAsync(channel, _options.StreamOptions.MaxLength.Value,
                useApproximateMaxLength: _options.StreamOptions.UseApproximateMaxLength);
        }

        return messageIds;
    }

    public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic) where TMessage : class
    {
        return new RedisPublisherBuilder<TMessage>(this, topic);
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_options.UsePubSub)
        {
            throw new NotSupportedException(
                "Subscription is currently only supported for Redis Pub/Sub. " +
                "Set UsePubSub=true in RedisOptions. Streams support will be added in a future version.");
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var subscriber = connection.GetSubscriber();

        // Determine channel based on routing pattern
        RedisChannel channel;
        if (options?.RoutingPatterns != null && options.RoutingPatterns.Count > 0)
        {
            // TODO: Redis doesn't support multiple pattern subscriptions in a single call
            // For now, use the first pattern. In the future, we could create multiple subscriptions
            var pattern = RoutingPatternMatcher.ConvertToRedisPattern(options.RoutingPatterns[0]);
            channel = RedisChannel.Pattern($"{_options.KeyPrefix}{pattern}");
            _logger.LogDebug("Subscribing to Redis with pattern: {Pattern}", pattern);
        }
        else if (options?.RoutingPattern != null)
        {
            var pattern = RoutingPatternMatcher.ConvertToRedisPattern(options.RoutingPattern);
            channel = RedisChannel.Pattern($"{_options.KeyPrefix}{pattern}");
            _logger.LogDebug("Subscribing to Redis with pattern: {Pattern}", pattern);
        }
        else
        {
            channel = RedisChannel.Literal($"{_options.KeyPrefix}{topic}");
        }

        // Create semaphore to enforce MaxConcurrency
        var maxConcurrency = options?.MaxConcurrency ?? 1;
        var concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        // Track processing tasks to await them during disposal
        var processingTasks = new System.Collections.Concurrent.ConcurrentBag<Task>();

        // Create the message handler (synchronous wrapper to avoid async void)
        Action<RedisChannel, RedisValue> messageHandler = (ch, value) =>
        {
            // Track processing task to await during disposal
            var processingTask = Task.Run(async () =>
            {
                // Enforce concurrency limit
                await concurrencySemaphore.WaitAsync(cancellationToken);
                try
                {
                    await HandleMessageAsync(value, topic, handler, options, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in Redis message handler for topic '{Topic}'", topic);
                }
                finally
                {
                    concurrencySemaphore.Release();
                }
            }, cancellationToken);

            // Track the task so we can await it during disposal
            processingTasks.Add(processingTask);
        };

        // Subscribe to the channel
        await subscriber.SubscribeAsync(channel, messageHandler);

        _logger.LogInformation(
            "Subscribed to Redis topic '{Topic}' on channel '{Channel}' (MaxConcurrency: {MaxConcurrency})",
            topic, channel, maxConcurrency);

        return new RedisSubscription(topic, subscriber, channel, messageHandler, concurrencySemaphore, processingTasks, _logger);
    }

    private async Task HandleMessageAsync<TMessage>(
        RedisValue value,
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options,
        CancellationToken cancellationToken) where TMessage : class
    {
        try
        {
            var envelope = _serializer.Deserialize<MessageEnvelope<TMessage>>(value);

            var context = new MessageContext<TMessage>
            {
                Envelope = envelope,
                Topic = topic,
                ConsumerGroup = options?.ConsumerGroup,
                DeliveryCount = 1, // Redis Pub/Sub doesn't track delivery count
                CancellationToken = cancellationToken
            };

            var result = await _pipeline.ExecuteConsumeAsync(context, handler, cancellationToken);

            // Note: Redis Pub/Sub doesn't support acks/nacks
            // Success/Retry/Reject results are logged but don't affect delivery
            if (result.Status != MessageStatus.Success)
            {
                _logger.LogWarning(
                    "Message processing resulted in {Status} for topic '{Topic}'. " +
                    "Note: Redis Pub/Sub does not support message acknowledgment or redelivery. Reason: {Reason}",
                    result.Status, topic, result.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from Redis topic '{Topic}'", topic);
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
        try
        {
            return _connection != null && _connection.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    private MessageEnvelope<TMessage> CreateEnvelope<TMessage>(TMessage message, PublishOptions? options) where TMessage : class
    {
        var headers = options?.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString() ?? string.Empty) ?? new Dictionary<string, string>();

        // Store routing key in headers if provided
        if (options?.RoutingKey != null)
        {
            headers["X-RoutingKey"] = options.RoutingKey;
        }

        return new MessageEnvelope<TMessage>
        {
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = options?.CorrelationId,
            CausationId = options?.CausationId,
            Source = options?.Source,
            MessageType = typeof(TMessage).FullName ?? typeof(TMessage).Name,
            Timestamp = DateTimeOffset.UtcNow,
            Headers = headers,
            Payload = message,
            Priority = options?.Priority ?? 0,
            TimeToLive = options?.TimeToLive,
            DeliveryDelay = options?.DeliveryDelay,
            Persistent = options?.Persistent ?? true
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _connectionLock.Dispose();

        _logger.LogInformation("Redis message bus '{Name}' disposed", _name);
    }
}
