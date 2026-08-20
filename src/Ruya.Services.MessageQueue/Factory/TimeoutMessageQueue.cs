using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.Factory;

/// <summary>
/// Applies the configured operation deadline without requiring every provider to duplicate
/// timeout and caller-cancellation semantics.
/// </summary>
internal sealed class TimeoutMessageQueue : IMessageQueue
{
    private readonly IMessageQueue _inner;
    private readonly TimeSpan _defaultTimeout;

    public TimeoutMessageQueue(IMessageQueue inner, TimeSpan defaultTimeout)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _defaultTimeout = ValidateTimeout(defaultTimeout, nameof(defaultTimeout));
    }

    public string Name => _inner.Name;

    public string Provider => _inner.Provider;

    public Task<string> PublishAsync<TMessage>(
        string topic,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var timeout = ResolvePublishTimeout(options);
        return ExecuteAsync(
            token => _inner.PublishAsync(topic, message, options, token),
            timeout,
            "publish",
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
        string topic,
        IEnumerable<TMessage> messages,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var timeout = ResolvePublishTimeout(options);
        return ExecuteAsync(
            token => _inner.PublishBatchAsync(topic, messages, options, token),
            timeout,
            "batch publish",
            cancellationToken);
    }

    public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic)
        where TMessage : class
    {
        return new TimeoutPublisherBuilder<TMessage>(this, topic);
    }

    public Task<IMessageSubscription> SubscribeAsync<TMessage>(
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // Providers may retain this token for the complete subscription lifetime so that host
        // shutdown reaches in-flight handlers. A linked setup-deadline token would be disposed as
        // soon as this method returns and would sever that lifetime cancellation relationship.
        return _inner.SubscribeAsync(topic, handler, options, cancellationToken);
    }

    public Task<IMessageSubscription> SubscribeAsync<TMessage>(
        IEnumerable<string> topics,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        return _inner.SubscribeAsync(topics, handler, options, cancellationToken);
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            _inner.IsHealthyAsync,
            _defaultTimeout,
            "health check",
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _inner.DisposeAsync();
    }

    internal static async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        TimeSpan timeout,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateTimeout(timeout, nameof(timeout));

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            return await operation(timeoutCancellation.Token);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (OperationCanceledException ex) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Message queue {operationName} did not complete within {timeout}.",
                ex);
        }
    }

    private TimeSpan ResolvePublishTimeout(PublishOptions? options)
    {
        return options?.Timeout is { } timeout
            ? ValidateTimeout(timeout, nameof(PublishOptions.Timeout))
            : _defaultTimeout;
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        return timeout > TimeSpan.Zero
            ? timeout
            : throw new ArgumentOutOfRangeException(parameterName, "Timeout must be greater than zero.");
    }

    private sealed class TimeoutPublisherBuilder<TMessage> : IMessagePublisherBuilder<TMessage>
        where TMessage : class
    {
        private readonly TimeoutMessageQueue _queue;
        private readonly string _topic;
        private readonly PublishOptions _options = new();

        public TimeoutPublisherBuilder(TimeoutMessageQueue queue, string topic)
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
