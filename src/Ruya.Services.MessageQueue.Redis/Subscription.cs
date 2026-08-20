using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ruya.Services.MessageQueue.Abstractions;
using StackExchange.Redis;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Redis implementation of <see cref="IMessageSubscription"/>.
/// </summary>
internal sealed class RedisSubscription : IMessageSubscription
{
    private static readonly EventId PausedEvent = new(3101, "RedisSubscriptionPaused");
    private static readonly EventId ResumedEvent = new(3102, "RedisSubscriptionResumed");
    private static readonly EventId UnsubscribedEvent = new(3103, "RedisSubscriptionUnsubscribed");
    private static readonly EventId LifetimeDisposalFailedEvent = new(3104, "RedisSubscriptionLifetimeDisposalFailed");
    private static readonly EventId ProcessingFailedEvent = new(3105, "RedisSubscriptionProcessingFailed");
    private static readonly EventId UnsubscribeFailedEvent = new(3106, "RedisSubscriptionUnsubscribeFailed");

    private readonly string _topic;
    private readonly ISubscriber _subscriber;
    private readonly IReadOnlyList<RedisChannel> _channels;
    private readonly Action<RedisChannel, RedisValue> _handler;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly RedisProcessingTaskTracker _processingTasks;
    private readonly CancellationTokenSource _lifetimeTokenSource;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _lifetimeMonitor;
    private int _pauseCount;
    private int _disposeState;
    private volatile bool _isActive = true;

    public RedisSubscription(
        string topic,
        ISubscriber subscriber,
        IReadOnlyList<RedisChannel> channels,
        Action<RedisChannel, RedisValue> handler,
        SemaphoreSlim concurrencySemaphore,
        RedisProcessingTaskTracker processingTasks,
        CancellationTokenSource lifetimeTokenSource,
        ILogger logger)
    {
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
        _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        if (_channels.Count == 0)
        {
            throw new ArgumentException("At least one Redis channel is required.", nameof(channels));
        }
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _concurrencySemaphore = concurrencySemaphore ?? throw new ArgumentNullException(nameof(concurrencySemaphore));
        _processingTasks = processingTasks ?? throw new ArgumentNullException(nameof(processingTasks));
        _lifetimeTokenSource = lifetimeTokenSource ?? throw new ArgumentNullException(nameof(lifetimeTokenSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        SubscriptionId = Guid.NewGuid().ToString();
        _lifetimeMonitor = StopWhenLifetimeEndsAsync();
    }

    public string SubscriptionId { get; }

    public IReadOnlyList<string> Topics => [_topic];

    public bool IsActive => _isActive && Volatile.Read(ref _disposeState) == 0;

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            if (Interlocked.CompareExchange(ref _pauseCount, 1, 0) != 0)
            {
                return;
            }

            try
            {
                await UnsubscribeAllAsync(cancellationToken);
                _isActive = false;
                _logger.LogInformation(PausedEvent, "Redis subscription '{SubscriptionId}' paused", SubscriptionId);
            }
            catch
            {
                Interlocked.Exchange(ref _pauseCount, 0);
                throw;
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            if (Interlocked.CompareExchange(ref _pauseCount, 0, 1) != 1)
            {
                return;
            }

            try
            {
                await SubscribeAllAsync(cancellationToken);
                _isActive = true;
                _logger.LogInformation(ResumedEvent, "Redis subscription '{SubscriptionId}' resumed", SubscriptionId);
            }
            catch
            {
                Interlocked.Exchange(ref _pauseCount, 1);
                throw;
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) == 0)
        {
            await DisposeCoreAsync();
            return;
        }

        await _disposeCompletion.Task;
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            _isActive = false;
            await _lifetimeTokenSource.CancelAsync();

            await _transitionLock.WaitAsync(CancellationToken.None);
            try
            {
                foreach (var channel in _channels)
                {
                    try
                    {
                        await _subscriber.UnsubscribeAsync(channel, _handler);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(
                            UnsubscribeFailedEvent,
                            exception,
                            "Redis subscription '{SubscriptionId}' could not unsubscribe from channel '{Channel}' during disposal",
                            SubscriptionId,
                            channel);
                    }
                }

                _logger.LogInformation(
                    UnsubscribedEvent,
                    "Redis subscription '{SubscriptionId}' unsubscribed",
                    SubscriptionId);
            }
            finally
            {
                _transitionLock.Release();
            }

            await _processingTasks.WhenAllAsync();
            _concurrencySemaphore.Dispose();
            _transitionLock.Dispose();
            _lifetimeTokenSource.Dispose();
            _disposeCompletion.TrySetResult();
            GC.KeepAlive(_lifetimeMonitor);
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private async Task DisposeFromLifetimeAsync()
    {
        try
        {
            await DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                LifetimeDisposalFailedEvent,
                exception,
                "Redis subscription '{SubscriptionId}' failed while stopping after lifetime cancellation",
                SubscriptionId);
        }
    }

    private async Task SubscribeAllAsync(CancellationToken cancellationToken)
    {
        var subscribed = new List<RedisChannel>(_channels.Count);
        try
        {
            foreach (var channel in _channels)
            {
                await _subscriber.SubscribeAsync(channel, _handler).WaitAsync(cancellationToken);
                subscribed.Add(channel);
            }
        }
        catch
        {
            foreach (var channel in subscribed)
            {
                await _subscriber.UnsubscribeAsync(channel, _handler);
            }

            throw;
        }
    }

    private async Task UnsubscribeAllAsync(CancellationToken cancellationToken)
    {
        var unsubscribed = new List<RedisChannel>(_channels.Count);
        try
        {
            foreach (var channel in _channels)
            {
                await _subscriber.UnsubscribeAsync(channel, _handler).WaitAsync(cancellationToken);
                unsubscribed.Add(channel);
            }
        }
        catch
        {
            foreach (var channel in unsubscribed)
            {
                await _subscriber.SubscribeAsync(channel, _handler);
            }

            throw;
        }
    }

    private async Task StopWhenLifetimeEndsAsync()
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _lifetimeTokenSource.Token);
        }
        catch (OperationCanceledException) when (_lifetimeTokenSource.IsCancellationRequested)
        {
            // Lifetime cancellation is the signal to dispose and unsubscribe.
        }

        await DisposeFromLifetimeAsync();
    }

    internal sealed class RedisProcessingTaskTracker
    {
        private readonly ConcurrentDictionary<Task, byte> _tasks = new();
        private readonly ILogger _logger;
        private readonly string _topic;

        public RedisProcessingTaskTracker(string topic, ILogger logger)
        {
            _topic = topic;
            _logger = logger;
        }

        public void Track(Task task)
        {
            _tasks.TryAdd(task, 0);
            task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() => ObserveCompleted(task));
        }

        public async Task WhenAllAsync()
        {
            while (!_tasks.IsEmpty)
            {
                var tasks = _tasks.Keys.ToArray();
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch
                {
                    // ObserveAsync records each processing failure and removes the completed task.
                }

                await Task.Yield();
            }
        }

        private void ObserveCompleted(Task task)
        {
            try
            {
                if (task.IsFaulted)
                {
                    _logger.LogError(
                        ProcessingFailedEvent,
                        task.Exception.GetBaseException(),
                        "Unhandled exception in Redis message handler for topic '{Topic}'",
                        _topic);
                }
            }
            finally
            {
                _tasks.TryRemove(task, out _);
            }
        }
    }
}
