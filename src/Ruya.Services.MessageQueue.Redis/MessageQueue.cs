using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Utilities;
using Ruya.Services.MessageQueue.Telemetry;
using System.Collections.Generic;
using System;
using System.Threading;
using Ruya.Services.MessageQueue.Serialization;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Redis implementation of IMessageQueue
/// Note: This is a reference implementation showing the structure.
/// Full implementation would include Pub/Sub and Streams support.
/// </summary>
internal sealed class RedisMessageQueue : IMessageQueue
{
    private static readonly EventId DisconnectedConnectionEvent = new(3001, "RedisDisconnectedConnectionDisposed");
    private static readonly EventId ConnectionDisposalFailedEvent = new(3002, "RedisConnectionDisposalFailed");
    private static readonly EventId ConnectedEvent = new(3003, "RedisConnected");
    private static readonly EventId MessagePublishedEvent = new(3004, "RedisMessagePublished");
    private static readonly EventId StreamMessagePublishedEvent = new(3005, "RedisStreamMessagePublished");
    private static readonly EventId BatchPublishedEvent = new(3006, "RedisBatchPublished");
    private static readonly EventId PatternSubscriptionEvent = new(3007, "RedisPatternSubscription");
    private static readonly EventId SubscribedEvent = new(3008, "RedisSubscribed");
    private static readonly EventId UnsupportedResultEvent = new(3009, "RedisUnsupportedMessageResult");
    private static readonly EventId HealthCheckFailedEvent = new(3010, "RedisHealthCheckFailed");
    private static readonly EventId DisposedEvent = new(3011, "RedisDisposed");
    private static readonly EventId CanceledConnectionCleanupFailedEvent = new(3012, "RedisCanceledConnectionCleanupFailed");
    private static readonly EventId FailedSubscriptionCleanupEvent = new(3013, "RedisFailedSubscriptionCleanup");
    private readonly string _name;
    private readonly RedisOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly MessageQueueTelemetry _telemetry;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<Task, byte> _backgroundCleanups = new();
    private volatile ConnectionMultiplexer? _connection;
    private int _disposeState;

    public RedisMessageQueue(
        string name,
        IOptions<RedisOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        MessageQueueTelemetry telemetry,
        ILogger logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => _name;
    public string Provider => nameof(Ruya.Services.MessageQueue.Redis);

    private async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

        if (_connection != null && _connection.IsConnected)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_connection != null && _connection.IsConnected)
            {
                return _connection;
            }

            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

            // Dispose old connection if it exists and is disconnected to prevent connection leak
            if (_connection != null)
            {
                var disconnectedConnection = _connection;
                _connection = null;
                _logger.LogDebug(DisconnectedConnectionEvent, "Disposing disconnected Redis connection for bus '{Name}'", _name);
                try
                {
                    await disconnectedConnection.CloseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ConnectionDisposalFailedEvent, ex, "Error disposing old Redis connection for bus '{Name}'", _name);
                }
                finally
                {
                    await disconnectedConnection.DisposeAsync();
                }
            }

            var configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
            configOptions.ConnectTimeout = (int)_options.ConnectionTimeout.TotalMilliseconds;
            configOptions.SyncTimeout = (int)_options.SyncTimeout.TotalMilliseconds;
            configOptions.AbortOnConnectFail = _options.AbortOnConnectFail;
            configOptions.ConnectRetry = _options.RetryOnFailure ? _options.RetryCount : 0;

            var connectTask = ConnectAsync(configOptions);
            try
            {
                var connection = await connectTask.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _connection = connection;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StartBackgroundCleanup(() => DisposeCanceledConnectionAttemptAsync(connectTask));
                throw;
            }
            _logger.LogInformation(ConnectedEvent, "Redis connection established for bus '{Name}'", _name);

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var telemetry = _telemetry.StartPublish(CreateEnvelope(message, options), "redis", topic);
        try
        {
            var messageId = await _pipeline.ExecutePublishAsync(
                telemetry.Envelope,
                topic,
                async (env, destinationTopic) => await PublishInternalAsync(env, destinationTopic, cancellationToken),
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

    private async Task<string> PublishInternalAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        CancellationToken cancellationToken) where TMessage : class
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var db = connection.GetDatabase(_options.Database);

        string? configuredRoutingKey = null;
        var hasRoutingKey = envelope.Headers?.TryGetValue("X-RoutingKey", out configuredRoutingKey) == true;
        var routingKey = hasRoutingKey ? configuredRoutingKey! : topic;
        var channel = _options.UsePubSub
            ? hasRoutingKey
                ? GetRoutedChannelName(topic, routingKey)
                : GetLiteralTopicChannelName(topic)
            : $"{_options.KeyPrefix}{routingKey}";

        if (_options.UsePubSub)
        {
            // Pub/Sub implementation
            var payload = _serializer.Serialize(envelope);
            await db.PublishAsync(RedisChannel.Literal(channel), payload).WaitAsync(cancellationToken);

            _logger.LogDebug(MessagePublishedEvent, "Published message {MessageId} to Redis channel {Channel}", envelope.MessageId, channel);
        }
        else if (_options.UseStreams)
        {
            // Streams implementation
            var fields = new[]
            {
                new NameValueEntry("messageId", envelope.MessageId),
                new NameValueEntry("payload", _serializer.Serialize(envelope))
            };

            var messageId = await db.StreamAddAsync(channel, fields).WaitAsync(cancellationToken);

            _logger.LogDebug(StreamMessagePublishedEvent, "Added message to Redis stream {Stream}: {MessageId}", channel, messageId);

            // Trim stream if max length is specified
            if (_options.StreamOptions?.MaxLength.HasValue ?? false)
            {
                await db.StreamTrimAsync(channel, _options.StreamOptions.MaxLength.Value,
                    useApproximateMaxLength: _options.StreamOptions.UseApproximateMaxLength).WaitAsync(cancellationToken);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        RejectCallerAssignedBatchMessageId(options);

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return Array.Empty<string>();
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var db = connection.GetDatabase(_options.Database);
        var messageIds = new List<string>(messageList.Count);
        var streamChannels = new HashSet<string>(StringComparer.Ordinal);

        // Use Redis batch/pipeline for efficient bulk operations
        var batch = db.CreateBatch();
        var batchTasks = new List<Task>();
        var telemetryScopes = new List<MessageQueueTelemetry.PublishScope<TMessage>>(messageList.Count);
        var batchParent = Activity.Current?.Context ?? default;

        try
        {
            foreach (var message in messageList)
            {
                var telemetry = _telemetry.StartPublish(
                    CreateEnvelope(message, options),
                    "redis",
                    topic,
                    batchParent);
                telemetryScopes.Add(telemetry);
                var envelope = telemetry.Envelope;
                var messageId = await _pipeline.ExecutePublishAsync(
                    envelope,
                    topic,
                    (publishedEnvelope, destinationTopic) =>
                    {
                        string? configuredRoutingKey = null;
                        var hasRoutingKey = publishedEnvelope.Headers?.TryGetValue(
                            "X-RoutingKey",
                            out configuredRoutingKey) == true;
                        var routingKey = hasRoutingKey ? configuredRoutingKey! : destinationTopic;
                        var channel = _options.UsePubSub
                            ? hasRoutingKey
                                ? GetRoutedChannelName(destinationTopic, routingKey)
                                : GetLiteralTopicChannelName(destinationTopic)
                            : $"{_options.KeyPrefix}{routingKey}";
                        var payload = _serializer.Serialize(publishedEnvelope);

                        if (_options.UsePubSub)
                        {
                            batchTasks.Add(batch.PublishAsync(RedisChannel.Literal(channel), payload));
                        }
                        else if (_options.UseStreams)
                        {
                            var fields = new[]
                            {
                                new NameValueEntry("messageId", publishedEnvelope.MessageId),
                                new NameValueEntry("payload", payload)
                            };
                            batchTasks.Add(batch.StreamAddAsync(channel, fields));
                            streamChannels.Add(channel);
                        }

                        return Task.FromResult(publishedEnvelope.MessageId);
                    },
                    cancellationToken);
                messageIds.Add(messageId);
            }

            // Execute the entire batch as a pipeline (single round-trip)
            cancellationToken.ThrowIfCancellationRequested();
            batch.Execute();
            await Task.WhenAll(batchTasks).WaitAsync(cancellationToken);
            foreach (var telemetry in telemetryScopes)
            {
                telemetry.Complete();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            for (var index = 0; index < telemetryScopes.Count; index++)
            {
                if (index < batchTasks.Count && batchTasks[index].IsCompletedSuccessfully)
                {
                    telemetryScopes[index].Complete();
                }
                else
                {
                    telemetryScopes[index].Fail(ex);
                }
            }
            throw;
        }
        finally
        {
            foreach (var telemetry in telemetryScopes)
            {
                telemetry.Dispose();
            }
        }

        _logger.LogDebug(BatchPublishedEvent, "Batch published {Count} messages to Redis using pipeline", messageIds.Count);

        // Trim stream if needed (after batch completes)
        if (_options.UseStreams && (_options.StreamOptions?.MaxLength.HasValue ?? false))
        {
            foreach (var streamChannel in streamChannels)
            {
                await db.StreamTrimAsync(streamChannel, _options.StreamOptions.MaxLength.Value,
                    useApproximateMaxLength: _options.StreamOptions.UseApproximateMaxLength).WaitAsync(cancellationToken);
            }
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.UsePubSub)
        {
            throw new NotSupportedException(
                "Subscription is currently only supported for Redis Pub/Sub. " +
                "Set UsePubSub=true in RedisOptions. Streams support will be added in a future version.");
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var subscriber = connection.GetSubscriber();

        var routingPatterns = options?.RoutingPatterns?.Where(static pattern => !string.IsNullOrWhiteSpace(pattern)).ToArray()
            ?? (string.IsNullOrWhiteSpace(options?.RoutingPattern) ? [] : [options.RoutingPattern]);

        // Preserve the released literal channel for publishes without an explicit routing key.
        // Explicit routing keys use a topic-hashed namespace so pattern subscriptions cannot see
        // traffic from another logical topic. The shared matcher still owns * and # semantics.
        var channels = new[]
        {
            RedisChannel.Literal(GetLiteralTopicChannelName(topic)),
            RedisChannel.Pattern($"{GetRoutedChannelPrefix(topic)}*"),
        };
        if (routingPatterns.Length > 0)
        {
            _logger.LogDebug(
                PatternSubscriptionEvent,
                "Subscribing to Redis topic '{Topic}' with {PatternCount} routing patterns",
                topic,
                routingPatterns.Length);
        }

        // Create semaphore to enforce MaxConcurrency
        var maxConcurrency = options?.MaxConcurrency ?? 1;
        if (maxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), maxConcurrency, "MaxConcurrency must be at least one.");
        }

        var concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var processingTasks = new RedisSubscription.RedisProcessingTaskTracker(topic, _logger);
        var lifetimeTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lifetimeToken = lifetimeTokenSource.Token;

        // Create the message handler (synchronous wrapper to avoid async void)
        Action<RedisChannel, RedisValue> messageHandler = (ch, value) =>
        {
            var processingTask = ProcessRedisMessageAsync(
                ch,
                value,
                topic,
                routingPatterns,
                handler,
                options,
                concurrencySemaphore,
                lifetimeToken);
            processingTasks.Track(processingTask);
        };

        var subscribeTasks = channels
            .Select(channel => subscriber.SubscribeAsync(channel, messageHandler))
            .ToArray();
        try
        {
            await Task.WhenAll(subscribeTasks).WaitAsync(cancellationToken);
        }
        catch
        {
            await lifetimeTokenSource.CancelAsync();
            StartBackgroundCleanup(() => CleanupFailedSubscriptionAsync(
                    subscribeTasks,
                    subscriber,
                    channels,
                    messageHandler,
                    processingTasks,
                    concurrencySemaphore,
                    lifetimeTokenSource));
            throw;
        }

        _logger.LogInformation(
            SubscribedEvent,
            "Subscribed to Redis topic '{Topic}' on {ChannelCount} channels (MaxConcurrency: {MaxConcurrency})",
            topic, channels.Length, maxConcurrency);

        return new RedisSubscription(
            topic,
            subscriber,
            channels,
            messageHandler,
            concurrencySemaphore,
            processingTasks,
            lifetimeTokenSource,
            _logger);
    }

    private async Task CleanupFailedSubscriptionAsync(
        IReadOnlyList<Task> subscribeTasks,
        ISubscriber subscriber,
        IReadOnlyList<RedisChannel> channels,
        Action<RedisChannel, RedisValue> messageHandler,
        RedisSubscription.RedisProcessingTaskTracker processingTasks,
        SemaphoreSlim concurrencySemaphore,
        CancellationTokenSource lifetimeTokenSource)
    {
        var canDisposeHandlerResources = true;
        try
        {
            for (var index = 0; index < subscribeTasks.Count; index++)
            {
                var subscribed = false;
                try
                {
                    await subscribeTasks[index];
                    subscribed = true;
                }
                catch
                {
                    // The setup failure is already reported to the caller; this await only observes it.
                }

                if (subscribed)
                {
                    try
                    {
                        await subscriber.UnsubscribeAsync(channels[index], messageHandler);
                    }
                    catch (Exception exception)
                    {
                        // The provider may still retain the callback. Keep its captured resources alive
                        // rather than allowing a late delivery to touch disposed synchronization state.
                        canDisposeHandlerResources = false;
                        _logger.LogWarning(
                            FailedSubscriptionCleanupEvent,
                            exception,
                            "Redis subscription setup failed and its late subscription could not be removed from channel '{Channel}'",
                            channels[index]);
                    }
                }
            }

            await processingTasks.WhenAllAsync();
        }
        catch (Exception exception)
        {
            canDisposeHandlerResources = false;
            _logger.LogWarning(
                FailedSubscriptionCleanupEvent,
                exception,
                "Redis subscription resources could not be cleaned after setup failed; first channel was '{Channel}'",
                channels.Count > 0 ? channels[0].ToString() : string.Empty);
        }
        finally
        {
            if (canDisposeHandlerResources)
            {
                concurrencySemaphore.Dispose();
                lifetimeTokenSource.Dispose();
            }
        }
    }

    private async Task ProcessRedisMessageAsync<TMessage>(
        RedisChannel channel,
        RedisValue value,
        string topic,
        string[] routingPatterns,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options,
        SemaphoreSlim concurrencySemaphore,
        CancellationToken cancellationToken) where TMessage : class
    {
        var channelName = channel.ToString();
        var literalTopicChannel = GetLiteralTopicChannelName(topic);
        string routingKey;
        if (string.Equals(channelName, literalTopicChannel, StringComparison.Ordinal))
        {
            routingKey = topic;
        }
        else
        {
            var routedPrefix = GetRoutedChannelPrefix(topic);
            if (!channelName.StartsWith(routedPrefix, StringComparison.Ordinal))
            {
                return;
            }

            routingKey = channelName[routedPrefix.Length..];
        }

        if (routingPatterns.Length > 0 && !RoutingPatternMatcher.MatchesAny(routingKey, routingPatterns))
        {
            return;
        }

        await concurrencySemaphore.WaitAsync(cancellationToken);
        try
        {
            await HandleMessageAsync(value, topic, handler, options, cancellationToken);
        }
        finally
        {
            concurrencySemaphore.Release();
        }
    }

    private async Task HandleMessageAsync<TMessage>(
        RedisValue value,
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options,
        CancellationToken cancellationToken) where TMessage : class
    {
        using var telemetry = _telemetry.StartDelivery("redis", topic, options?.ConsumerGroup);
        try
        {
            var body = (byte[]?)value
                ?? throw new InvalidOperationException("Redis delivered an empty message body.");
            var envelope = _serializer.Deserialize<MessageEnvelope<TMessage>>(body);
            telemetry.AttachEnvelope(envelope, 1);

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
                    UnsupportedResultEvent,
                    "Message processing resulted in {Status} for topic '{Topic}'. " +
                    "Note: Redis Pub/Sub does not support message acknowledgment or redelivery. Reason: {Reason}",
                    result.Status, topic, result.Reason);
            }

            telemetry.Complete(result.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            telemetry.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            telemetry.Unhandled(ex);
            throw;
        }
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        IEnumerable<string> topics,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

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
            var connection = await GetConnectionAsync(cancellationToken);
            var latency = await connection.GetDatabase(_options.Database).PingAsync().WaitAsync(cancellationToken);
            return connection.IsConnected && latency >= TimeSpan.Zero;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(HealthCheckFailedEvent, exception, "Redis health check failed for bus '{Name}'", _name);
            return false;
        }
    }

    private static MessageEnvelope<TMessage> CreateEnvelope<TMessage>(TMessage message, PublishOptions? options) where TMessage : class
    {
        var headers = options?.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString() ?? string.Empty) ?? new Dictionary<string, string>();

        // Store routing key in headers if provided
        if (options?.RoutingKey != null)
        {
            headers["X-RoutingKey"] = options.RoutingKey;
        }

        return new MessageEnvelope<TMessage>
        {
            MessageId = ResolveMessageId(options),
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

    private string GetLiteralTopicChannelName(string topic) => $"{_options.KeyPrefix}{topic}";

    private string GetRoutedChannelName(string topic, string routingKey) =>
        $"{GetRoutedChannelPrefix(topic)}{routingKey}";

    private string GetRoutedChannelPrefix(string topic)
    {
        var topicHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(topic)));
        return $"{_options.KeyPrefix}__ruya_topic__:{topicHash}:";
    }

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            await _disposeCompletion.Task;
            return;
        }

        try
        {
            await _connectionLock.WaitAsync(CancellationToken.None);
            try
            {
                var connection = _connection;
                _connection = null;
                if (connection is not null)
                {
                    try
                    {
                        await connection.CloseAsync();
                    }
                    finally
                    {
                        await connection.DisposeAsync();
                    }
                }
            }
            finally
            {
                _connectionLock.Release();
            }

            _logger.LogInformation(DisposedEvent, "Redis message bus '{Name}' disposed", _name);
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
        finally
        {
            _connectionLock.Dispose();
        }
    }

    private async Task DisposeCanceledConnectionAttemptAsync(Task<ConnectionMultiplexer> connectTask)
    {
        try
        {
            var connection = await connectTask;
            try
            {
                await connection.CloseAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                CanceledConnectionCleanupFailedEvent,
                exception,
                "Canceled Redis connection attempt for bus '{Name}' did not produce a connection to dispose",
                _name);
        }
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(ConfigurationOptions configurationOptions)
    {
        return await ConnectionMultiplexer.ConnectAsync(configurationOptions);
    }

    private void StartBackgroundCleanup(Func<Task> cleanup)
    {
        var cleanupTask = RunBackgroundCleanupAsync(cleanup);
        _backgroundCleanups.TryAdd(cleanupTask, 0);
        cleanupTask.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
        {
            _backgroundCleanups.TryRemove(cleanupTask, out _);
            if (cleanupTask.IsFaulted)
            {
                _ = cleanupTask.Exception;
            }
        });
    }

    private static async Task RunBackgroundCleanupAsync(Func<Task> cleanup)
    {
        await cleanup();
    }
}
