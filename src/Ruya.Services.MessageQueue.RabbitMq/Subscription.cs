using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.RabbitMq;

internal sealed class RabbitMQSubscription : IMessageSubscription
{
    private string _consumerTag;  // Not readonly - reassigned in ResumeAsync
    private readonly string _topic;
    private readonly IChannel _channel;
    private readonly SemaphoreSlim? _concurrencySemaphore;
    private readonly ILogger _logger;
    private readonly AsyncEventingBasicConsumer _consumer;
    private readonly string _queueName;
    private readonly bool _autoAck;
    private int _pauseCount = 0;  // Track pause state to prevent race conditions
    private volatile bool _isActive;
    private volatile bool _disposed;

    public RabbitMQSubscription(
        string consumerTag,
        string topic,
        string queueName,
        IChannel channel,
        AsyncEventingBasicConsumer consumer,
        bool autoAck,
        SemaphoreSlim? concurrencySemaphore,
        ILogger logger)
    {
        _consumerTag = consumerTag ?? throw new ArgumentNullException(nameof(consumerTag));
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _autoAck = autoAck;
        _concurrencySemaphore = concurrencySemaphore;
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

        // Use Interlocked to prevent multiple pause calls from racing (atomic transition 0→1)
        if (Interlocked.CompareExchange(ref _pauseCount, 1, 0) == 0)
        {
            try
            {
                // Actually cancel the consumer to stop receiving messages
                await _channel.BasicCancelAsync(_consumerTag, false, cancellationToken);

                _isActive = false;
                _logger.LogInformation("Subscription '{SubscriptionId}' paused (consumer cancelled)", SubscriptionId);
            }
            catch (Exception ex)
            {
                // Restore state on error
                Interlocked.Exchange(ref _pauseCount, 0);
                _logger.LogError(ex, "Error pausing subscription '{SubscriptionId}'", SubscriptionId);
                throw;
            }
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RabbitMQSubscription));
        }

        // Use Interlocked to prevent multiple resume calls from racing (atomic transition 1→0)
        if (Interlocked.CompareExchange(ref _pauseCount, 0, 1) == 1)
        {
            try
            {
                // Resume by starting the consumer again
                _consumerTag = await _channel.BasicConsumeAsync(
                    queue: _queueName,
                    autoAck: _autoAck,
                    consumer: _consumer,
                    cancellationToken: cancellationToken);

                _isActive = true;
                _logger.LogInformation("Subscription '{SubscriptionId}' resumed (consumer restarted with tag '{ConsumerTag}')",
                    SubscriptionId, _consumerTag);
            }
            catch (Exception ex)
            {
                // Restore state on error
                Interlocked.Exchange(ref _pauseCount, 1);
                _logger.LogError(ex, "Error resuming subscription '{SubscriptionId}'", SubscriptionId);
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
            await _channel.BasicCancelAsync(_consumerTag, false);

            _logger.LogInformation("Subscription '{SubscriptionId}' cancelled", SubscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling subscription '{SubscriptionId}'", SubscriptionId);
        }

        // Small delay to allow in-flight handlers to complete
        // This is a best-effort approach since RabbitMQ's event model doesn't provide
        // a clean way to track active handlers
        await Task.Delay(100);

        // Dispose the channel (dedicated to this consumer)
        try
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing channel for subscription '{SubscriptionId}'", SubscriptionId);
        }

        // Dispose the concurrency semaphore (after delay to let handlers finish)
        _concurrencySemaphore?.Dispose();
    }
}
