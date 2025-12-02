using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Middleware;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;


namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// In-memory implementation of IMessageQueue using System.Threading.Channels
/// Supports both competing consumers (queue) and broadcast (pub/sub) patterns via ConsumerGroups
/// </summary>
internal sealed class InMemoryMessageQueue : IMessageQueue
{
    private readonly string _name;
    private readonly InMemoryOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly ILogger _logger;

    // Topics with consumer group management
    private readonly ConcurrentDictionary<string, TopicManager> _topics = new();

    // Dead letter queue
    private readonly Channel<DeadLetterMessage> _deadLetterQueue;

    // Message store for replay (optional)
    private readonly ConcurrentDictionary<string, ConcurrentQueue<StoredMessage>>? _messageStore;

    // Track delayed delivery tasks to await them during disposal
    private readonly ConcurrentBag<Task> _delayedDeliveryTasks = new();

    // Cancellation token for disposal to cancel delayed delivery tasks
    private readonly CancellationTokenSource _disposalCts = new();

    private volatile bool _disposed;

    public InMemoryMessageQueue(
        string name,
        IOptions<InMemoryOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _deadLetterQueue = Channel.CreateUnbounded<DeadLetterMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        if (_options.EnableMessageStore)
        {
            _messageStore = new ConcurrentDictionary<string, ConcurrentQueue<StoredMessage>>();
        }

        _logger.LogInformation(
            "InMemory message bus '{Name}' initialized with consumer group support (DLQ: {DLQ}, Store: {Store})",
            _name, _options.EnableDeadLetterQueue, _options.EnableMessageStore);
    }

    public string Name => _name;
    public string Provider => "InMemory";

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
        var topicManager = GetOrCreateTopic(topic);

        // Store message if enabled
        if (_messageStore != null)
        {
            StoreMessage(topic, envelope);
        }

        var wrapper = new MessageWrapper(
            _serializer.Serialize(envelope),
            envelope.MessageId,
            envelope.Priority,
            envelope.TimeToLive.HasValue ? DateTimeOffset.UtcNow.Add(envelope.TimeToLive.Value) : null,
            envelope.Headers?.TryGetValue("X-RoutingKey", out var routingKey) == true ? routingKey : topic);

        // Handle delayed delivery
        if (envelope.DeliveryDelay.HasValue)
        {
            // Track delayed delivery task to ensure we wait for it during disposal
            var delayedTask = Task.Run(async () =>
            {
                try
                {
                    // Use linked token that cancels on either caller cancel OR disposal
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, _disposalCts.Token);

                    await Task.Delay(envelope.DeliveryDelay.Value, linkedCts.Token);
                    if (!_disposed)
                    {
                        await topicManager.BroadcastAsync(wrapper, linkedCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Delayed delivery cancelled for message {MessageId}", envelope.MessageId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in delayed delivery for message {MessageId}", envelope.MessageId);
                }
            }, CancellationToken.None);  // Don't pass caller token to Task.Run itself

            _delayedDeliveryTasks.Add(delayedTask);
        }
        else
        {
            await topicManager.BroadcastAsync(wrapper, cancellationToken);
        }

        _logger.LogDebug(
            "Published message {MessageId} to topic '{Topic}' → {ConsumerGroupCount} consumer groups (Priority: {Priority})",
            envelope.MessageId, topic, topicManager.ConsumerGroupCount, envelope.Priority);

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

        var topicManager = GetOrCreateTopic(topic);
        var messageIds = new List<string>(messageList.Count);

        foreach (var message in messageList)
        {
            var envelope = CreateEnvelope(message, options);
            messageIds.Add(envelope.MessageId);

            if (_messageStore != null)
            {
                StoreMessage(topic, envelope);
            }

            var wrapper = new MessageWrapper(
                _serializer.Serialize(envelope),
                envelope.MessageId,
                envelope.Priority,
                null,
                envelope.Headers?.TryGetValue("X-RoutingKey", out var routingKey) == true ? routingKey : topic);

            await topicManager.BroadcastAsync(wrapper, cancellationToken);
        }

        _logger.LogDebug("Batch published {Count} messages to topic '{Topic}'", messageIds.Count, topic);

        return messageIds;
    }

    public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic) where TMessage : class
    {
        return new InMemoryPublisherBuilder<TMessage>(this, topic);
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var topicManager = GetOrCreateTopic(topic);
        var consumerGroup = options?.ConsumerGroup ?? $"default-{Guid.NewGuid():N}"; // Unique default = broadcast

        // Warn if consumer group already exists (possible duplicate subscription)
        var consumerGroupChannel = topicManager.GetOrAddConsumerGroup(consumerGroup, _options.ChannelCapacity);

        if (options?.ConsumerGroup != null && topicManager.ConsumerGroupCount > 1)
        {
            _logger.LogWarning(
                "Creating additional subscription for existing consumer group '{ConsumerGroup}' on topic '{Topic}'. " +
                "If this is unintended, it may cause duplicate message processing.",
                consumerGroup, topic);
        }

        var subscription = new InMemorySubscription<TMessage>(
            topic,
            consumerGroup,
            consumerGroupChannel.Reader,
            handler,
            options,
            _serializer,
            _pipeline,
            _options,
            _deadLetterQueue.Writer,
            _logger);

        await subscription.StartAsync(cancellationToken);

        _logger.LogInformation(
            "Subscribed to topic '{Topic}' [ConsumerGroup: '{ConsumerGroup}'] (Pattern: {Pattern}, MaxConcurrency: {MaxConcurrency})",
            topic,
            consumerGroup,
            options?.ConsumerGroup == null ? "Broadcast" : "Competing",
            options?.MaxConcurrency ?? 1);

        return subscription;
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

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(!_disposed);
    }

    private TopicManager GetOrCreateTopic(string topic)
    {
        return _topics.GetOrAdd(topic, t =>
        {
            _logger.LogDebug("Created topic '{Topic}'", t);
            return new TopicManager(t, _logger);
        });
    }

    private void StoreMessage<TMessage>(string topic, MessageEnvelope<TMessage> envelope) where TMessage : class
    {
        if (_messageStore == null) return;

        var queue = _messageStore.GetOrAdd(topic, _ => new ConcurrentQueue<StoredMessage>());

        queue.Enqueue(new StoredMessage(
            envelope.MessageId,
            _serializer.Serialize(envelope),
            DateTimeOffset.UtcNow));

        // Limit stored messages
        while (queue.Count > _options.MaxStoredMessagesPerTopic)
        {
            queue.TryDequeue(out _);
        }
    }

    private MessageEnvelope<TMessage> CreateEnvelope<TMessage>(TMessage message, PublishOptions? options)
        where TMessage : class
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
            MessageType = typeof(TMessage).FullName ?? typeof(TMessage).Name,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = message,
            CorrelationId = options?.CorrelationId,
            CausationId = options?.CausationId,
            Source = options?.Source,
            Headers = headers,
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

        _logger.LogInformation("Disposing InMemory message bus '{Name}' with {TopicCount} topics", _name, _topics.Count);

        // Cancel all delayed delivery tasks to prevent long waits
        _disposalCts.Cancel();

        // Wait for all delayed delivery tasks to complete (should complete quickly due to cancellation)
        if (_delayedDeliveryTasks.Count > 0)
        {
            _logger.LogDebug("Waiting for {Count} delayed delivery tasks to complete", _delayedDeliveryTasks.Count);
            try
            {
                await Task.WhenAll(_delayedDeliveryTasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Delayed delivery tasks cancelled during disposal");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for delayed delivery tasks during disposal");
            }
        }

        // Dispose the disposal CancellationTokenSource
        _disposalCts.Dispose();

        // Complete all topic managers
        foreach (var topicManager in _topics.Values)
        {
            topicManager.Complete();
        }

        _deadLetterQueue.Writer.Complete();

        _topics.Clear();
        _messageStore?.Clear();

        _logger.LogInformation("InMemory message bus '{Name}' disposed", _name);
    }

    // Internal classes

    /// <summary>
    /// Manages consumer groups for a single topic
    /// Implements fanout/broadcast to multiple consumer groups
    /// </summary>
    private sealed class TopicManager
    {
        private readonly string _topic;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, Channel<MessageWrapper>> _consumerGroups = new();

        public TopicManager(string topic, ILogger logger)
        {
            _topic = topic;
            _logger = logger;
        }

        public int ConsumerGroupCount => _consumerGroups.Count;

        public Channel<MessageWrapper> GetOrAddConsumerGroup(string consumerGroup, int? channelCapacity)
        {
            return _consumerGroups.GetOrAdd(consumerGroup, cg =>
            {
                var channel = channelCapacity.HasValue
                    ? Channel.CreateBounded<MessageWrapper>(new BoundedChannelOptions(channelCapacity.Value)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = false,
                        SingleWriter = false
                    })
                    : Channel.CreateUnbounded<MessageWrapper>(new UnboundedChannelOptions
                    {
                        SingleReader = false,
                        SingleWriter = false
                    });

                _logger.LogDebug(
                    "Created consumer group '{ConsumerGroup}' for topic '{Topic}' (Capacity: {Capacity})",
                    cg, _topic, channelCapacity?.ToString() ?? "unbounded");

                return channel;
            });
        }

        public async Task BroadcastAsync(MessageWrapper message, CancellationToken cancellationToken)
        {
            if (_consumerGroups.IsEmpty)
            {
                _logger.LogWarning("No consumer groups for topic '{Topic}', message {MessageId} dropped",
                    _topic, message.MessageId);
                return;
            }

            // Broadcast to ALL consumer groups (fanout pattern)
            var tasks = new List<Task>(_consumerGroups.Count);

            foreach (var channel in _consumerGroups.Values)
            {
                tasks.Add(channel.Writer.WriteAsync(message, cancellationToken).AsTask());
            }

            await Task.WhenAll(tasks);
        }

        public void Complete()
        {
            foreach (var channel in _consumerGroups.Values)
            {
                channel.Writer.Complete();
            }
        }
    }

    private sealed record StoredMessage(
        string MessageId,
        byte[] SerializedMessage,  // Changed from string to byte[] to match MessageWrapper type
        DateTimeOffset Timestamp);


}
