using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Ruya.Services.MessageQueue.Abstractions;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Redis implementation of IMessageSubscription
/// </summary>
internal sealed class RedisSubscription : IMessageSubscription
{
    private readonly string _topic;
    private readonly ISubscriber _subscriber;
    private readonly RedisChannel _channel;
    private readonly Action<RedisChannel, RedisValue> _handler;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim? _concurrencySemaphore;
    private readonly ConcurrentBag<Task>? _processingTasks;  // Track active processing tasks
    private int _pauseCount = 0;  // Track pause state to prevent multiple pause/resume calls
    private volatile bool _isActive;
    private volatile bool _disposed;

    public RedisSubscription(
        string topic,
        ISubscriber subscriber,
        RedisChannel channel,
        Action<RedisChannel, RedisValue> handler,
        SemaphoreSlim? concurrencySemaphore,
        ConcurrentBag<Task>? processingTasks,
        ILogger logger)
    {
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
        _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        _channel = channel;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _concurrencySemaphore = concurrencySemaphore;
        _processingTasks = processingTasks;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isActive = true;

        SubscriptionId = Guid.NewGuid().ToString();
    }

    public string SubscriptionId { get; }

    public IReadOnlyList<string> Topics => new[] { _topic };

    public bool IsActive => _isActive && !_disposed;

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        // Use Interlocked to prevent multiple pause calls
        if (Interlocked.CompareExchange(ref _pauseCount, 1, 0) == 0)
        {
            try
            {
                // Unsubscribe from the channel to stop receiving messages
                await _subscriber.UnsubscribeAsync(_channel, _handler);
                _isActive = false;
                _logger.LogInformation("Redis subscription '{SubscriptionId}' paused", SubscriptionId);
            }
            catch (Exception ex)
            {
                // Restore state on error
                Interlocked.Exchange(ref _pauseCount, 0);
                _logger.LogError(ex, "Error pausing Redis subscription '{SubscriptionId}'", SubscriptionId);
                throw;
            }
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RedisSubscription));
        }

        // Use Interlocked to prevent multiple resume calls
        if (Interlocked.CompareExchange(ref _pauseCount, 0, 1) == 1)
        {
            try
            {
                // Resubscribe to the channel to resume receiving messages
                await _subscriber.SubscribeAsync(_channel, _handler);
                _isActive = true;
                _logger.LogInformation("Redis subscription '{SubscriptionId}' resumed", SubscriptionId);
            }
            catch (Exception ex)
            {
                // Restore state on error
                Interlocked.Exchange(ref _pauseCount, 1);
                _logger.LogError(ex, "Error resuming Redis subscription '{SubscriptionId}'", SubscriptionId);
                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        // Set flags FIRST to prevent new operations
        _disposed = true;
        _isActive = false;

        try
        {
            // Unsubscribe from the channel (stops new messages)
            await _subscriber.UnsubscribeAsync(_channel, _handler);
            _logger.LogInformation("Redis subscription '{SubscriptionId}' unsubscribed", SubscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unsubscribing Redis subscription '{SubscriptionId}'", SubscriptionId);
        }

        // Wait for all active processing tasks to complete BEFORE disposing semaphore
        if (_processingTasks != null && _processingTasks.Count > 0)
        {
            _logger.LogDebug("Waiting for {Count} active processing tasks to complete for subscription '{SubscriptionId}'",
                _processingTasks.Count, SubscriptionId);

            try
            {
                await Task.WhenAll(_processingTasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Processing tasks cancelled during disposal");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for processing tasks during disposal for subscription '{SubscriptionId}'",
                    SubscriptionId);
            }
        }

        // NOW safe to dispose semaphore (all tasks are fully stopped)
        _concurrencySemaphore?.Dispose();
    }
}
