using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// A message retained by the in-memory provider after terminal delivery failure.
/// </summary>
public sealed record InMemoryDeadLetterMessage(
    string QueueName,
    string Topic,
    string MessageId,
    ReadOnlyMemory<byte> SerializedMessage,
    string Reason,
    int AttemptCount,
    DateTimeOffset Timestamp);

/// <summary>
/// Provides bounded storage, inspection, and dequeue access to dead letters retained by named
/// in-memory queues.
/// </summary>
public interface IInMemoryDeadLetterStore
{
    /// <summary>
    /// Stores a dead letter. When the configured capacity is reached, the oldest retained message
    /// for that named queue is discarded.
    /// </summary>
    void Store(InMemoryDeadLetterMessage message);

    /// <summary>
    /// Returns a stable oldest-to-newest snapshot for the specified queue.
    /// </summary>
    IReadOnlyList<InMemoryDeadLetterMessage> GetSnapshot(string queueName);

    /// <summary>
    /// Removes and returns the oldest retained dead letter for the specified queue.
    /// </summary>
    bool TryDequeue(string queueName, out InMemoryDeadLetterMessage? message);
}

internal sealed class InMemoryDeadLetterStore : IInMemoryDeadLetterStore
{
    private readonly ConcurrentDictionary<string, BoundedDeadLetterBuffer> _queues =
        new(StringComparer.Ordinal);
    private readonly int _capacity;

    public InMemoryDeadLetterStore(IOptions<InMemoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _capacity = options.Value.DeadLetterQueueCapacity;
    }

    public IReadOnlyList<InMemoryDeadLetterMessage> GetSnapshot(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return _queues.TryGetValue(queueName, out var queue)
            ? queue.GetSnapshot()
            : Array.Empty<InMemoryDeadLetterMessage>();
    }

    public bool TryDequeue(string queueName, out InMemoryDeadLetterMessage? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        if (_queues.TryGetValue(queueName, out var queue) && queue.TryDequeue(out message))
        {
            return true;
        }

        message = null;
        return false;
    }

    public void Store(InMemoryDeadLetterMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _queues.GetOrAdd(message.QueueName, _ => new BoundedDeadLetterBuffer(_capacity)).Add(message);
    }

    private sealed class BoundedDeadLetterBuffer
    {
        private readonly int _capacity;
        private readonly Queue<InMemoryDeadLetterMessage> _messages = new();
        private readonly object _gate = new();

        public BoundedDeadLetterBuffer(int capacity)
        {
            _capacity = capacity;
        }

        public void Add(InMemoryDeadLetterMessage message)
        {
            lock (_gate)
            {
                _messages.Enqueue(message);
                while (_messages.Count > _capacity)
                {
                    _messages.Dequeue();
                }
            }
        }

        public IReadOnlyList<InMemoryDeadLetterMessage> GetSnapshot()
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }

        public bool TryDequeue(out InMemoryDeadLetterMessage? message)
        {
            lock (_gate)
            {
                return _messages.TryDequeue(out message);
            }
        }
    }
}
