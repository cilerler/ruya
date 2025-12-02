using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Common;
using Ruya.Services.DistributedLock.Redis.Configuration;
using StackExchange.Redis;

namespace Ruya.Services.DistributedLock.Redis.Providers;

/// <summary>
/// Redlock implementation of distributed lock provider.
/// Implements the Redlock algorithm for distributed consensus across multiple independent Redis instances.
/// </summary>
public sealed class RedlockProvider : IDistributedLockProvider, IDisposable
{
    private readonly List<IConnectionMultiplexer> _connections;
    private readonly ILogger<RedlockProvider> _logger;
    private readonly int _quorum;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedlockProvider"/> class.
    /// </summary>
    /// <param name="settings">The Redis lock settings.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="connectionFactory">Optional factory for creating Redis connections (for testing).</param>
    /// <param name="multiplexer">Optional existing multiplexer for single-instance mode.</param>
    public RedlockProvider(
        IOptions<RedisLockSettings> settings,
        ILogger<RedlockProvider> logger,
        Func<ConfigurationOptions, IConnectionMultiplexer>? connectionFactory = null,
        IConnectionMultiplexer? multiplexer = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        var redisSettings = settings.Value;
        _connections = new List<IConnectionMultiplexer>();

        if (redisSettings.RedlockEndpoints != null && redisSettings.RedlockEndpoints.Length > 0)
        {
            // Multi-Master Mode (Redlock)
            foreach (var endpoint in redisSettings.RedlockEndpoints)
            {
                var configOptions = ConfigurationOptions.Parse(endpoint);
                configOptions.SyncTimeout = redisSettings.SyncTimeoutMs;
                configOptions.AbortOnConnectFail = redisSettings.AbortOnConnectFail;
                
                var connection = connectionFactory?.Invoke(configOptions) ?? ConnectionMultiplexer.Connect(configOptions);
                _connections.Add(connection);
            }
        }
        else if (multiplexer != null)
        {
            // Single-Instance Mode (Fallback)
            // Use the existing multiplexer as the single node
            _connections.Add(multiplexer);
        }
        else
        {
            throw new ArgumentException("Either RedlockEndpoints must be provided or an IConnectionMultiplexer must be registered.", nameof(settings));
        }

        // Quorum is N/2 + 1
        _quorum = (_connections.Count / 2) + 1;
    }

    /// <inheritdoc />
    public async Task<bool> AcquireLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        var acquiredCount = 0;
        var startTime = DateTime.UtcNow;

        // Drift is 1% of TTL + small constant (e.g. 2ms) to account for clock skew
        var drift = TimeSpan.FromMilliseconds((expiry.TotalMilliseconds * 0.01) + 2);

        // Try to acquire lock on all instances sequentially (or parallel)
        // Redlock recommends parallel for performance, but sequential is safer for simple impl.
        // We'll use parallel tasks for better performance.
        var tasks = _connections.Select(async conn =>
        {
            try
            {
                var db = conn.GetDatabase();
                return await db.LockTakeAsync(lockKey, lockValue, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error acquiring lock on Redis instance: {Endpoint}", conn.Configuration);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        acquiredCount = results.Count(r => r);

        var validityTime = expiry - (DateTime.UtcNow - startTime) - drift;

        if (acquiredCount >= _quorum && validityTime > TimeSpan.Zero)
        {
            return true;
        }

        // Failed to acquire quorum or took too long -> Unlock all
        await ReleaseLockAsync(lockKey, lockValue, cancellationToken);
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ExtendLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        var extendedCount = 0;
        var tasks = _connections.Select(async conn =>
        {
            try
            {
                var db = conn.GetDatabase();
                return await db.LockExtendAsync(lockKey, lockValue, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extending lock on Redis instance: {Endpoint}", conn.Configuration);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        extendedCount = results.Count(r => r);

        // If we maintained quorum, we are good
        return extendedCount >= _quorum;
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseLockAsync(
        string lockKey,
        string lockValue,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Release on ALL instances, even if we think we didn't acquire it there
        var tasks = _connections.Select(async conn =>
        {
            try
            {
                var db = conn.GetDatabase();
                return await db.LockReleaseAsync(lockKey, lockValue);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing lock on Redis instance: {Endpoint}", conn.Configuration);
                return false;
            }
        });

        await Task.WhenAll(tasks);
        return true; // Always return true as we made best effort to release
    }

    /// <inheritdoc />
    public async Task<bool> LockExistsAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var existsCount = 0;
        var tasks = _connections.Select(async conn =>
        {
            try
            {
                var db = conn.GetDatabase();
                return await db.KeyExistsAsync(lockKey);
            }
            catch
            {
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        existsCount = results.Count(r => r);

        return existsCount >= _quorum;
    }

    /// <inheritdoc />
    public string GetProviderName() => "Redlock";

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        foreach (var conn in _connections)
        {
            try
            {
                conn.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        _disposed = true;
    }
}
