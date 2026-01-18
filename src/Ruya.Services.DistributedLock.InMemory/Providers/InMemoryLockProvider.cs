using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Common;

namespace Ruya.Services.DistributedLock.InMemory.Providers;

/// <summary>
/// In-memory implementation of distributed lock provider.
/// Useful for testing and single-instance scenarios.
/// Thread-safe implementation using ConcurrentDictionary.
/// </summary>
public sealed class InMemoryLockProvider : IDistributedLockProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    private sealed class LockEntry
    {
        private DateTimeOffset _expiresAt;
        private readonly object _lock = new();

        public string Value { get; init; } = string.Empty;

        public DateTimeOffset ExpiresAt
        {
            get
            {
                lock (_lock)
                {
                    return _expiresAt;
                }
            }
            set
            {
                lock (_lock)
                {
                    _expiresAt = value;
                }
            }
        }

        public bool IsExpired() => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryLockProvider"/> class.
    /// </summary>
    public InMemoryLockProvider()
    {
        // Run cleanup every 10 seconds to remove expired locks
        _cleanupTimer = new Timer(
            CleanupExpiredLocks,
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    /// <inheritdoc />
    public Task<bool> AcquireLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        var newEntry = new LockEntry { Value = lockValue, ExpiresAt = expiresAt };

        // Try to add the lock if it doesn't exist
        if (_locks.TryAdd(lockKey, newEntry))
        {
            return Task.FromResult(true);
        }

        // Lock exists - check if it's expired and try to replace it
        if (_locks.TryGetValue(lockKey, out var existingEntry) && existingEntry.IsExpired())
        {
            // Try to replace the expired lock atomically
            if (_locks.TryUpdate(lockKey, newEntry, existingEntry))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> ExtendLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_locks.TryGetValue(lockKey, out var entry))
        {
            return Task.FromResult(false);
        }

        // Only extend if the lock value matches and hasn't expired
        // The Value comparison is safe because it's init-only
        // The ExpiresAt access is thread-safe due to the property lock
        if (entry.Value != lockValue || entry.IsExpired())
        {
            return Task.FromResult(false);
        }

        // Extend the expiry (thread-safe due to property lock)
        entry.ExpiresAt = DateTimeOffset.UtcNow.Add(expiry);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> ReleaseLockAsync(
        string lockKey,
        string lockValue,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        // Validate lock exists and value matches before attempting removal
        if (!_locks.TryGetValue(lockKey, out var entry))
        {
            return Task.FromResult(false);
        }

        if (entry.Value != lockValue)
        {
            return Task.FromResult(false);
        }

        // Atomic compare-and-swap removal using the exact instance we validated
        var removed = _locks.TryRemove(new KeyValuePair<string, LockEntry>(lockKey, entry));
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<bool> LockExistsAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        // Check if lock exists and hasn't expired
        var exists = _locks.TryGetValue(lockKey, out var entry) && !entry.IsExpired();

        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public Task<bool> ForceReleaseLockAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        // Safely remove the lock if it exists, regardless of value or state
        if (_locks.TryRemove(lockKey, out var entry))
        {
            return Task.FromResult(true);
        }

        // Lock didn't exist, so technically it's "released" (or wasn't there)
        // Returning true because the end state (lock is gone) is achieved.
        // However, many "Force" implementations return true if they *actually* deleted something.
        // Looking at Redis implementation: return deletedCount >= _quorum.
        // So if it returns 0 deleted, it returns false.
        // Let's mimic that behavior: return true only if we actually removed it.
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public string GetProviderName() => "InMemory";

    /// <summary>
    /// Clears all locks. Useful for testing.
    /// </summary>
    public void ClearAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _locks.Clear();
    }

    /// <summary>
    /// Gets the count of active locks.
    /// </summary>
    public int LockCount => _locks.Count;

    private void CleanupExpiredLocks(object? state)
    {
        if (_disposed) return;

        var expiredKeys = _locks
            .Where(kvp => kvp.Value.IsExpired())
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            // Only remove if it's still expired (double-check to avoid race conditions)
            if (_locks.TryGetValue(key, out var entry) && entry.IsExpired())
            {
                // Use atomic compare-and-swap to only remove the exact instance we validated
                _locks.TryRemove(new KeyValuePair<string, LockEntry>(key, entry));
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _cleanupTimer.Dispose();
        _locks.Clear();
    }
}
