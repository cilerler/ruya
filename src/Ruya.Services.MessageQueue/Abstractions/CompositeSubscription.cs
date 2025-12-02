using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.MessageQueue.Abstractions;

/// <summary>
/// Composite subscription that manages multiple underlying subscriptions
/// Used for multi-topic subscriptions
/// </summary>
public sealed class CompositeSubscription : IMessageSubscription
{
    private readonly IReadOnlyList<IMessageSubscription> _subscriptions;
    private readonly List<Exception> _errors;
    private readonly object _lock = new object();  // Lock for IsActive check and error collection to prevent TOCTOU race
    private volatile bool _disposed;

    public CompositeSubscription(IEnumerable<IMessageSubscription> subscriptions)
    {
        _subscriptions = subscriptions?.ToList() ?? throw new ArgumentNullException(nameof(subscriptions));
        _errors = new List<Exception>();

        if (_subscriptions.Count == 0)
        {
            throw new ArgumentException("Must have at least one subscription", nameof(subscriptions));
        }

        SubscriptionId = Guid.NewGuid().ToString();

        // Collect all topics from all subscriptions
        Topics = _subscriptions.SelectMany(s => s.Topics).Distinct().ToList();
    }

    public string SubscriptionId { get; }

    public IReadOnlyList<string> Topics { get; }

    public bool IsActive
    {
        get
        {
            // Lock to prevent TOCTOU race condition where subscription states change between checks
            lock (_lock)
            {
                if (_disposed) return false;
                return _subscriptions.All(s => s.IsActive);
            }
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            _errors.Clear();
        }

        // Pause all subscriptions in parallel
        var tasks = _subscriptions.Select(async s =>
        {
            try
            {
                await s.PauseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _errors.Add(ex);
                }
            }
        });

        await Task.WhenAll(tasks);

        lock (_lock)
        {
            if (_errors.Count > 0)
            {
                throw new AggregateException(
                    $"Failed to pause {_errors.Count} of {_subscriptions.Count} subscriptions",
                    _errors);
            }
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CompositeSubscription));
        }

        lock (_lock)
        {
            _errors.Clear();
        }

        // Resume all subscriptions in parallel
        var tasks = _subscriptions.Select(async s =>
        {
            try
            {
                await s.ResumeAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _errors.Add(ex);
                }
            }
        });

        await Task.WhenAll(tasks);

        lock (_lock)
        {
            if (_errors.Count > 0)
            {
                throw new AggregateException(
                    $"Failed to resume {_errors.Count} of {_subscriptions.Count} subscriptions",
                    _errors);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose all subscriptions in parallel
        var tasks = _subscriptions.Select(async s =>
        {
            try
            {
                await s.DisposeAsync();
            }
            catch
            {
                // Swallow disposal errors to ensure all subscriptions are disposed
            }
        });

        await Task.WhenAll(tasks);
    }
}
