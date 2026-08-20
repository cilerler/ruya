using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Telemetry;
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
    private readonly MessageQueueTelemetry _telemetry;
    private readonly ILogger _logger;

    // Topics with consumer group management
    private readonly ConcurrentDictionary<string, TopicManager> _topics = new();

    private readonly IInMemoryDeadLetterStore _deadLetterStore;

    // Message store for replay (optional)
    private readonly ConcurrentDictionary<string, ConcurrentQueue<StoredMessage>>? _messageStore;

    // Track delayed delivery tasks to await them during disposal
    private readonly ConcurrentDictionary<Task, byte> _delayedDeliveryTasks = new();

    // Cancellation token for disposal to cancel delayed delivery tasks
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly object _lifecycleGate = new();
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _disposeState;

    public InMemoryMessageQueue(
        string name,
        IOptions<InMemoryOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        MessageQueueTelemetry telemetry,
        IInMemoryDeadLetterStore deadLetterStore,
        ILogger logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _deadLetterStore = deadLetterStore ?? throw new ArgumentNullException(nameof(deadLetterStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.EnableMessageStore)
        {
            _messageStore = new ConcurrentDictionary<string, ConcurrentQueue<StoredMessage>>();
        }

        _logger.LogInformation(
            InMemoryLogEvents.QueueLifecycle,
            "InMemory message bus '{Name}' initialized with consumer group support (DLQ: {DLQ}, Store: {Store})",
            _name, _options.EnableDeadLetterQueue, _options.EnableMessageStore);
    }

    public string Name => _name;
    public string Provider => nameof(Ruya.Services.MessageQueue.InMemory);

    public async Task<string> PublishAsync<TMessage>(
        string topic,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var telemetry = _telemetry.StartPublish(CreateEnvelope(message, options), "in_memory", topic);
        try
        {
            var messageId = await _pipeline.ExecutePublishAsync(
                telemetry.Envelope,
                topic,
                async (env, t) => await PublishInternalAsync(env, t, cancellationToken),
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
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

                // Once PublishAsync reports acceptance, the caller token no longer owns the scheduled
                // delivery. Queue disposal remains the lifetime boundary for in-memory delayed work.
                var delayedTask = DeliverDelayedAsync(
                    topicManager,
                    wrapper,
                    envelope.DeliveryDelay.Value,
                    envelope.MessageId);
                _delayedDeliveryTasks.TryAdd(delayedTask, 0);
                _ = RemoveDelayedDeliveryWhenCompleteAsync(delayedTask);
            }
        }
        else
        {
            await topicManager.BroadcastAsync(wrapper, cancellationToken);
        }

        _logger.LogDebug(
            InMemoryLogEvents.Publish,
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        RejectCallerAssignedBatchMessageId(options);

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return Array.Empty<string>();
        }

        var messageIds = new List<string>(messageList.Count);

        foreach (var message in messageList)
        {
            messageIds.Add(await PublishAsync(topic, message, options, cancellationToken));
        }

        _logger.LogDebug(InMemoryLogEvents.Publish, "Batch published {Count} messages to topic '{Topic}'", messageIds.Count, topic);

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var topicManager = GetOrCreateTopic(topic);
        var consumerGroup = options?.ConsumerGroup ?? $"default-{Guid.NewGuid():N}"; // Unique default = broadcast

        // Warn if consumer group already exists (possible duplicate subscription)
        var consumerGroupLease = topicManager.AcquireConsumerGroup(
            consumerGroup,
            _options.ChannelCapacity,
            removeWhenUnused: options?.ConsumerGroup is null);

        if (options?.ConsumerGroup is not null && consumerGroupLease.WasExisting)
        {
            _logger.LogDebug(
                InMemoryLogEvents.Subscription,
                "Adding a competing subscription to consumer group '{ConsumerGroup}' on topic '{Topic}'.",
                consumerGroup, topic);
        }

        var subscription = new InMemorySubscription<TMessage>(
            topic,
            consumerGroup,
            consumerGroupLease.Buffer,
            consumerGroupLease.Release,
            handler,
            options,
            _serializer,
            _pipeline,
            _options,
            _name,
            _deadLetterStore,
            _telemetry,
            _logger);

        try
        {
            await subscription.StartAsync(cancellationToken);
        }
        catch
        {
            await subscription.DisposeAsync();
            throw;
        }

        _logger.LogInformation(
            InMemoryLogEvents.Subscription,
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

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _disposeState) == 0);
    }

    private TopicManager GetOrCreateTopic(string topic)
    {
        return _topics.GetOrAdd(topic, t =>
        {
            _logger.LogDebug(InMemoryLogEvents.Topology, "Created topic '{Topic}'", t);
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
            MessageId = ResolveMessageId(options),
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

    private async Task DeliverDelayedAsync(
        TopicManager topicManager,
        MessageWrapper wrapper,
        TimeSpan delay,
        string messageId)
    {
        try
        {
            await Task.Delay(delay, _disposalCts.Token);
            await topicManager.BroadcastAsync(wrapper, _disposalCts.Token);
        }
        catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
        {
            _logger.LogDebug(InMemoryLogEvents.DelayedDelivery, "Delayed delivery cancelled during queue disposal for message {MessageId}", messageId);
        }
        catch (Exception exception)
        {
            _logger.LogError(InMemoryLogEvents.DelayedDelivery, exception, "Error in delayed delivery for message {MessageId}", messageId);
        }
    }

    private async Task RemoveDelayedDeliveryWhenCompleteAsync(Task delayedTask)
    {
        try
        {
            await delayedTask;
        }
        finally
        {
            _delayedDeliveryTasks.TryRemove(delayedTask, out _);
        }
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
            // Synchronize with delayed-publish acceptance so every accepted task is visible below.
            Task[] delayedTasks;
            lock (_lifecycleGate)
            {
                delayedTasks = _delayedDeliveryTasks.Keys.ToArray();
            }

            _logger.LogInformation(InMemoryLogEvents.QueueLifecycle, "Disposing InMemory message bus '{Name}' with {TopicCount} topics", _name, _topics.Count);

            await _disposalCts.CancelAsync();

            if (delayedTasks.Length > 0)
            {
                _logger.LogDebug(InMemoryLogEvents.DelayedDelivery, "Waiting for {Count} delayed delivery tasks to complete", delayedTasks.Length);
                try
                {
                    await Task.WhenAll(delayedTasks);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug(InMemoryLogEvents.DelayedDelivery, "Delayed delivery tasks cancelled during disposal");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(InMemoryLogEvents.DelayedDelivery, ex, "Error waiting for delayed delivery tasks during disposal");
                }
            }

            _disposalCts.Dispose();

            foreach (var topicManager in _topics.Values)
            {
                topicManager.Complete();
            }

            _topics.Clear();
            _messageStore?.Clear();

            _logger.LogInformation(InMemoryLogEvents.QueueLifecycle, "InMemory message bus '{Name}' disposed", _name);
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
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
        private readonly Dictionary<string, ConsumerGroupState> _consumerGroups = new(StringComparer.Ordinal);
        private readonly object _consumerGroupsLock = new();

        public TopicManager(string topic, ILogger logger)
        {
            _topic = topic;
            _logger = logger;
        }

        public int ConsumerGroupCount
        {
            get
            {
                lock (_consumerGroupsLock)
                {
                    return _consumerGroups.Count;
                }
            }
        }

        public ConsumerGroupLease AcquireConsumerGroup(
            string consumerGroup,
            int? channelCapacity,
            bool removeWhenUnused)
        {
            lock (_consumerGroupsLock)
            {
                var wasExisting = _consumerGroups.TryGetValue(consumerGroup, out var state);
                if (!wasExisting)
                {
                    var buffer = new ConsumerGroupBuffer(channelCapacity);
                    state = new ConsumerGroupState(buffer, removeWhenUnused);
                    _consumerGroups.Add(consumerGroup, state);

                    _logger.LogDebug(
                        InMemoryLogEvents.Topology,
                        "Created consumer group '{ConsumerGroup}' for topic '{Topic}' (Capacity: {Capacity})",
                        consumerGroup,
                        _topic,
                        channelCapacity?.ToString() ?? "unbounded");
                }

                state!.SubscriberCount++;
                var capturedState = state;
                return new ConsumerGroupLease(
                    state.Buffer,
                    wasExisting,
                    () => ReleaseConsumerGroup(consumerGroup, capturedState));
            }
        }

        private void ReleaseConsumerGroup(string consumerGroup, ConsumerGroupState state)
        {
            lock (_consumerGroupsLock)
            {
                if (state.SubscriberCount > 0)
                {
                    state.SubscriberCount--;
                }

                if (state.SubscriberCount == 0 &&
                    state.RemoveWhenUnused &&
                    _consumerGroups.TryGetValue(consumerGroup, out var current) &&
                    ReferenceEquals(current, state))
                {
                    _consumerGroups.Remove(consumerGroup);
                }
            }
        }

        public async Task BroadcastAsync(MessageWrapper message, CancellationToken cancellationToken)
        {
            ConsumerGroupState[] consumerGroups;
            lock (_consumerGroupsLock)
            {
                consumerGroups = _consumerGroups.Values.ToArray();
            }

            if (consumerGroups.Length == 0)
            {
                _logger.LogWarning(InMemoryLogEvents.Topology, "No consumer groups for topic '{Topic}', message {MessageId} dropped",
                    _topic, message.MessageId);
                return;
            }

            var tasks = new List<Task>(consumerGroups.Length);
            foreach (var consumerGroup in consumerGroups)
            {
                tasks.Add(consumerGroup.Buffer.WriteAsync(message, cancellationToken).AsTask());
            }

            await Task.WhenAll(tasks);
        }

        public void Complete()
        {
            ConsumerGroupState[] consumerGroups;
            lock (_consumerGroupsLock)
            {
                consumerGroups = _consumerGroups.Values.ToArray();
                _consumerGroups.Clear();
            }

            foreach (var consumerGroup in consumerGroups)
            {
                consumerGroup.Buffer.Complete();
            }
        }

        private sealed class ConsumerGroupState
        {
            public ConsumerGroupState(ConsumerGroupBuffer buffer, bool removeWhenUnused)
            {
                Buffer = buffer;
                RemoveWhenUnused = removeWhenUnused;
            }

            public ConsumerGroupBuffer Buffer { get; }

            public bool RemoveWhenUnused { get; }

            public int SubscriberCount { get; set; }
        }

        public sealed class ConsumerGroupLease
        {
            private Action? _release;

            public ConsumerGroupLease(ConsumerGroupBuffer buffer, bool wasExisting, Action release)
            {
                Buffer = buffer;
                WasExisting = wasExisting;
                _release = release;
            }

            public ConsumerGroupBuffer Buffer { get; }

            public bool WasExisting { get; }

            public void Release()
            {
                Interlocked.Exchange(ref _release, null)?.Invoke();
            }
        }
    }

    private sealed record StoredMessage(
        string MessageId,
        byte[] SerializedMessage,  // Changed from string to byte[] to match MessageWrapper type
        DateTimeOffset Timestamp);


}
