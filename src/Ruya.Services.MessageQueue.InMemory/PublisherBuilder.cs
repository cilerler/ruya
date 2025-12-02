using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.InMemory;

internal sealed class InMemoryPublisherBuilder<TMessage> : IMessagePublisherBuilder<TMessage> where TMessage : class
{
    private readonly InMemoryMessageQueue _bus;
    private readonly string _topic;
    private readonly PublishOptions _options;

    public InMemoryPublisherBuilder(InMemoryMessageQueue bus, string topic)
    {
        _bus = bus;
        _topic = topic;
        _options = new PublishOptions();
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
        if (headers == null) throw new ArgumentNullException(nameof(headers));

        _options.Headers ??= new Dictionary<string, object>();

        foreach (var header in headers)
        {
            _options.Headers[header.Key] = header.Value;
        }
        return this;
    }

    public Task<string> SendAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        return _bus.PublishAsync(_topic, message, _options, cancellationToken);
    }

    public Task<IReadOnlyList<string>> SendBatchAsync(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
    {
        return _bus.PublishBatchAsync(_topic, messages, _options, cancellationToken);
    }
}
