using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.Factory;

/// <summary>
/// Compatibility proxy for the former synchronous singleton registration helper.
/// Queue creation begins on the first asynchronous operation and never blocks service resolution.
/// </summary>
internal sealed class AsyncLazyMessageQueue : IMessageQueue
{
    private readonly string _name;
    private readonly string? _configuredProvider;
    private readonly Lazy<Task<IMessageQueue>> _queue;
    private IMessageQueue? _resolvedQueue;
    private bool _disposed;

    public AsyncLazyMessageQueue(
        string name,
        string? configuredProvider,
        IMessageQueueFactory factory)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _configuredProvider = configuredProvider;
        ArgumentNullException.ThrowIfNull(factory);

        _queue = new Lazy<Task<IMessageQueue>>(
            () => InitializeAsync(factory),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Name => _name;

    public string Provider => Volatile.Read(ref _resolvedQueue)?.Provider
        ?? _configuredProvider
        ?? "Deferred";

    public async Task<string> PublishAsync<TMessage>(
        string topic,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var queue = await GetQueueAsync(cancellationToken);
        return await queue.PublishAsync(topic, message, options, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
        string topic,
        IEnumerable<TMessage> messages,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var queue = await GetQueueAsync(cancellationToken);
        return await queue.PublishBatchAsync(topic, messages, options, cancellationToken);
    }

    public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic)
        where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new AsyncLazyPublisherBuilder<TMessage>(this, topic);
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var queue = await GetQueueAsync(cancellationToken);
        return await queue.SubscribeAsync(topic, handler, options, cancellationToken);
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        IEnumerable<string> topics,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var queue = await GetQueueAsync(cancellationToken);
        return await queue.SubscribeAsync(topics, handler, options, cancellationToken);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var queue = await GetQueueAsync(cancellationToken);
        return await queue.IsHealthyAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        // MessageQueueFactory owns and disposes the cached provider queue. The proxy must not
        // dispose the same instance a second time when the DI container is torn down.
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private async Task<IMessageQueue> InitializeAsync(IMessageQueueFactory factory)
    {
        var queue = await factory.CreateQueueAsync(_name, CancellationToken.None);
        Volatile.Write(ref _resolvedQueue, queue);
        return queue;
    }

    private async Task<IMessageQueue> GetQueueAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var queueTask = _queue.Value;
        return cancellationToken.CanBeCanceled
            ? await queueTask.WaitAsync(cancellationToken)
            : await queueTask;
    }

    private sealed class AsyncLazyPublisherBuilder<TMessage> : IMessagePublisherBuilder<TMessage>
        where TMessage : class
    {
        private readonly AsyncLazyMessageQueue _queue;
        private readonly string _topic;
        private readonly PublishOptions _options = new();

        public AsyncLazyPublisherBuilder(AsyncLazyMessageQueue queue, string topic)
        {
            _queue = queue;
            _topic = topic;
        }

        public IMessagePublisherBuilder<TMessage> WithPriority(byte priority)
        {
            _options.Priority = priority;
            return this;
        }

        public IMessagePublisherBuilder<TMessage> WithTimeToLive(TimeSpan ttl)
        {
            _options.TimeToLive = ttl;
            return this;
        }

        public IMessagePublisherBuilder<TMessage> WithDelay(TimeSpan delay)
        {
            _options.DeliveryDelay = delay;
            return this;
        }

        public IMessagePublisherBuilder<TMessage> WithPersistence(bool persistent = true)
        {
            _options.Persistent = persistent;
            return this;
        }

        public IMessagePublisherBuilder<TMessage> WithCorrelationId(string correlationId)
        {
            _options.CorrelationId = correlationId;
            return this;
        }

        public IMessagePublisherBuilder<TMessage> WithCausationId(string causationId)
        {
            _options.CausationId = causationId;
            return this;
        }

        public IMessagePublisherBuilder<TMessage> WithSource(string source)
        {
            _options.Source = source;
            return this;
        }

        public IMessagePublisherBuilder<TMessage> WithHeader(string key, string value)
        {
            _options.Headers ??= new Dictionary<string, object>();
            _options.Headers[key] = value;
            return this;
        }

        public IMessagePublisherBuilder<TMessage> WithHeaders(IDictionary<string, string> headers)
        {
            ArgumentNullException.ThrowIfNull(headers);
            _options.Headers ??= new Dictionary<string, object>();

            foreach (var header in headers)
            {
                _options.Headers[header.Key] = header.Value;
            }

            return this;
        }

        public Task<string> SendAsync(
            TMessage message,
            CancellationToken cancellationToken = default)
        {
            return _queue.PublishAsync(_topic, message, _options, cancellationToken);
        }

        public Task<IReadOnlyList<string>> SendBatchAsync(
            IEnumerable<TMessage> messages,
            CancellationToken cancellationToken = default)
        {
            return _queue.PublishBatchAsync(_topic, messages, _options, cancellationToken);
        }
    }
}
