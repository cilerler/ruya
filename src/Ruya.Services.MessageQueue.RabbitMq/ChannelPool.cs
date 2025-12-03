using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.MessageQueue.RabbitMq;

/// <summary>
/// Thread-safe pool for RabbitMQ channels.
/// RabbitMQ channels are NOT thread-safe and must not be shared across concurrent operations.
/// This pool ensures each concurrent operation gets its own channel.
/// </summary>
internal sealed class ChannelPool : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly int _maxSize;
    private readonly ILogger _logger;
    private readonly ConcurrentBag<IChannel> _channels;
    private readonly SemaphoreSlim _semaphore;
    private readonly bool _enablePublisherConfirms;
    private bool _disposed;

    public ChannelPool(IConnection connection, int maxSize, bool enablePublisherConfirms, ILogger logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _maxSize = maxSize > 0 ? maxSize : throw new ArgumentOutOfRangeException(nameof(maxSize), "Pool size must be greater than 0");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channels = new ConcurrentBag<IChannel>();
        _semaphore = new SemaphoreSlim(maxSize, maxSize);
        _enablePublisherConfirms = enablePublisherConfirms;

        _logger.LogDebug("ChannelPool created with max size: {MaxSize}", _maxSize);
    }

    /// <summary>
    /// Borrows a channel from the pool for exclusive use.
    /// The caller MUST return the channel using Return() after use.
    /// </summary>
    public async Task<IChannel> BorrowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Wait for an available slot in the pool
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            // Try to get an existing channel from the pool
            while (_channels.TryTake(out var channel))
            {
                if (channel.IsOpen)
                {
                    _logger.LogTrace("Borrowed existing channel from pool (IsOpen: {IsOpen})", channel.IsOpen);
                    return channel;
                }
                else
                {
                    // Channel is closed, dispose it
                    try
                    {
                        channel.Dispose();
                        _logger.LogDebug("Disposed closed channel from pool");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing closed channel");
                    }
                }
            }

            // No available channels, create a new one
            var options = new CreateChannelOptions(_enablePublisherConfirms, false);

            // Retry loop for connection recovery
            var retryCount = 0;
            while (true)
            {
                try
                {
                    var newChannel = await _connection.CreateChannelAsync(options, cancellationToken);
                    _logger.LogTrace("Created new channel for pool");
                    return newChannel;
                }
                catch (Exception ex)
                {
                    // Check if it's a connection issue (AlreadyClosedException or similar)
                    if (ex is global::RabbitMQ.Client.Exceptions.AlreadyClosedException ||
                        ex is global::RabbitMQ.Client.Exceptions.BrokerUnreachableException ||
                        ex is IOException)
                    {
                        retryCount++;
                        if (retryCount > 30) throw; // Give up after 30 retries

                        _logger.LogWarning(ex, "Connection issue creating channel, waiting for recovery (Attempt {Attempt}/30)...", retryCount);
                        await Task.Delay(1000, cancellationToken);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
        catch
        {
            // If we fail to get/create a channel, release the semaphore slot
            _semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// Returns a borrowed channel back to the pool.
    /// </summary>
    public void Return(IChannel channel)
    {
        if (channel == null) throw new ArgumentNullException(nameof(channel));

        try
        {
            if (_disposed)
            {
                // Pool is disposed, just dispose the channel
                try
                {
                    channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing channel during pool disposal");
                }
                return;
            }

            if (channel.IsOpen)
            {
                // Return the channel to the pool for reuse
                _channels.Add(channel);
                _logger.LogTrace("Returned channel to pool");
            }
            else
            {
                // Channel is closed, dispose it instead of returning to pool
                channel.Dispose();
                _logger.LogDebug("Disposed closed channel instead of returning to pool");
            }
        }
        finally
        {
            // Always release the semaphore slot, even if pool is disposed
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        _logger.LogDebug("Disposing ChannelPool with {Count} channels", _channels.Count);

        // Dispose all channels in the pool
        while (_channels.TryTake(out var channel))
        {
            try
            {
                if (channel.IsOpen)
                {
                    await channel.CloseAsync();
                }
                channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing channel from pool");
            }
        }

        _semaphore.Dispose();

        _logger.LogInformation("ChannelPool disposed");
    }
}
