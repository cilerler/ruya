using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// Applies publisher backpressure while retaining an unbounded internal redelivery path. A delivery
/// returned during subscription cancellation must never compete with publishers for a bounded slot.
/// </summary>
internal sealed class ConsumerGroupBuffer
{
    private readonly Channel<BufferedMessage> _channel = Channel.CreateUnbounded<BufferedMessage>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });
    private readonly SemaphoreSlim? _publishCapacity;

    public ConsumerGroupBuffer(int? capacity)
    {
        _publishCapacity = capacity.HasValue
            ? new SemaphoreSlim(capacity.Value, capacity.Value)
            : null;
    }

    public async ValueTask WriteAsync(MessageWrapper message, CancellationToken cancellationToken)
    {
        if (_publishCapacity is null)
        {
            await _channel.Writer.WriteAsync(new BufferedMessage(message, OccupiesPublishCapacity: false), cancellationToken);
            return;
        }

        await _publishCapacity.WaitAsync(cancellationToken);
        try
        {
            await _channel.Writer.WriteAsync(new BufferedMessage(message, OccupiesPublishCapacity: true), cancellationToken);
        }
        catch
        {
            _publishCapacity.Release();
            throw;
        }
    }

    public bool TryReturn(MessageWrapper message) =>
        _channel.Writer.TryWrite(new BufferedMessage(message, OccupiesPublishCapacity: false));

    public async IAsyncEnumerable<MessageWrapper> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var bufferedMessage in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (bufferedMessage.OccupiesPublishCapacity)
            {
                _publishCapacity!.Release();
            }

            yield return bufferedMessage.Message;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();

    private sealed record BufferedMessage(MessageWrapper Message, bool OccupiesPublishCapacity);
}
