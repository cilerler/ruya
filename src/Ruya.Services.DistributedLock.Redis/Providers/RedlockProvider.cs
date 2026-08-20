using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly List<IConnectionMultiplexer> _ownedConnections;
    private readonly ILogger<RedlockProvider> _logger;
    private readonly int _quorum;
    private int _disposeState;

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

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
        : this(
            GetSettings(settings),
            GetSettings(settings).RedlockEndpoints ?? [],
            logger,
            connectionFactory,
            multiplexer)
    {
    }

    internal RedlockProvider(
        IOptions<RedisLockSettings> settings,
        IReadOnlyList<string> resolvedEndpoints,
        ILogger<RedlockProvider> logger,
        Func<ConfigurationOptions, IConnectionMultiplexer>? connectionFactory = null,
        IConnectionMultiplexer? multiplexer = null)
        : this(GetSettings(settings), resolvedEndpoints, logger, connectionFactory, multiplexer)
    {
    }

    private RedlockProvider(
        RedisLockSettings redisSettings,
        IReadOnlyList<string> resolvedEndpoints,
        ILogger<RedlockProvider> logger,
        Func<ConfigurationOptions, IConnectionMultiplexer>? connectionFactory,
        IConnectionMultiplexer? multiplexer)
    {
        ArgumentNullException.ThrowIfNull(redisSettings);
        ArgumentNullException.ThrowIfNull(resolvedEndpoints);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _connections = new List<IConnectionMultiplexer>();
        _ownedConnections = new List<IConnectionMultiplexer>();

        if (resolvedEndpoints.Count > 0)
        {
            if (!RedlockEndpointSetValidator.IsValid(resolvedEndpoints))
            {
                throw new ArgumentException(
                    "Redlock requires an odd number of at least three independent single-node Redis endpoints.",
                    nameof(resolvedEndpoints));
            }

            // Multi-Master Mode (Redlock)
            try
            {
                foreach (string endpoint in resolvedEndpoints)
                {
                    var configOptions = ConfigurationOptions.Parse(endpoint);
                    configOptions.SyncTimeout = redisSettings.SyncTimeoutMs;
                    configOptions.AbortOnConnectFail = redisSettings.AbortOnConnectFail;

                    var connection = connectionFactory?.Invoke(configOptions) ?? ConnectionMultiplexer.Connect(configOptions);
                    _connections.Add(connection);
                    _ownedConnections.Add(connection);
                }
            }
            catch
            {
                foreach (IConnectionMultiplexer connection in _ownedConnections)
                {
                    try
                    {
                        connection.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        _logger.LogWarning(LogEvents.OwnedConnectionDisposeFailed, cleanupException, "Error disposing an owned Redis connection after initialization failed");
                    }
                }

                throw;
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
            throw new ArgumentException(
                "Either RedlockEndpoints must be provided or an IConnectionMultiplexer must be registered.",
                nameof(resolvedEndpoints));
        }

        // Quorum is N/2 + 1
        _quorum = (_connections.Count / 2) + 1;
    }

    private static RedisLockSettings GetSettings(IOptions<RedisLockSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Value;
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
        ExpiryValidation.Validate(expiry);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        var acquisitionStopwatch = Stopwatch.StartNew();

        // Drift is 1% of TTL + small constant (e.g. 2ms) to account for clock skew
        var drift = TimeSpan.FromMilliseconds((expiry.TotalMilliseconds * 0.01) + 2);

        // Try to acquire lock on all instances sequentially (or parallel)
        // Redlock recommends parallel for performance, but sequential is safer for simple impl.
        // We'll use parallel tasks for better performance.
        var tasks = _connections.Select(async (conn, nodeIndex) =>
        {
            try
            {
                var db = conn.GetDatabase();
                return await db.LockTakeAsync(lockKey, lockValue, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(LogEvents.NodeAcquireFailed, ex, "Error acquiring lock on Redis node {NodeIndex}", nodeIndex);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        int acquiredCount = results.Count(r => r);

        if (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAcrossNodesAsync(lockKey, lockValue);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var validityTime = expiry - acquisitionStopwatch.Elapsed - drift;

        if (acquiredCount >= _quorum && validityTime > TimeSpan.Zero)
        {
            return true;
        }

        // Failed to acquire quorum or took too long -> Unlock all
        await ReleaseAcrossNodesAsync(lockKey, lockValue);
        cancellationToken.ThrowIfCancellationRequested();
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
        ExpiryValidation.Validate(expiry);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        var extensionStopwatch = Stopwatch.StartNew();
        var tasks = _connections.Select(async (conn, nodeIndex) =>
        {
            try
            {
                var db = conn.GetDatabase();
                return await db.LockExtendAsync(lockKey, lockValue, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(LogEvents.NodeExtendFailed, ex, "Error extending lock on Redis node {NodeIndex}", nodeIndex);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        int extendedCount = results.Count(r => r);
        var drift = TimeSpan.FromMilliseconds((expiry.TotalMilliseconds * 0.01) + 2);
        var validityTime = expiry - extensionStopwatch.Elapsed - drift;
        bool extended = extendedCount >= _quorum && validityTime > TimeSpan.Zero;
        if (!extended)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseLockAsync(
        string lockKey,
        string lockValue,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        bool[] results = await ReleaseAcrossNodesAsync(lockKey, lockValue);
        cancellationToken.ThrowIfCancellationRequested();
        return results.Count(released => released) >= _quorum;
    }

    /// <inheritdoc />
    public async Task<bool> LockExistsAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var existsCount = 0;
        var tasks = _connections.Select(async (conn, nodeIndex) =>
        {
            try
            {
                var db = conn.GetDatabase();
                return await db.KeyExistsAsync(lockKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(LogEvents.NodeExistsFailed, ex, "Error checking lock existence on Redis node {NodeIndex}", nodeIndex);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();
        existsCount = results.Count(r => r);

        return existsCount >= _quorum;
    }

    /// <inheritdoc />
    public async Task<bool> ForceReleaseLockAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        // Force delete on ALL instances
        var tasks = _connections.Select(async (conn, nodeIndex) =>
        {
            try
            {
                var db = conn.GetDatabase();
                return await db.KeyDeleteAsync(lockKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(LogEvents.NodeForceReleaseFailed, ex, "Error force releasing lock on Redis node {NodeIndex}", nodeIndex);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();
        var deletedCount = results.Count(r => r);

        return deletedCount >= _quorum;
    }

    /// <inheritdoc />
    public string GetProviderName() => "Redlock";

    private async Task<bool[]> ReleaseAcrossNodesAsync(string lockKey, string lockValue)
    {
        // Cleanup deliberately has no caller token: cancellation must not strand a lock
        // on nodes that completed acquisition before the caller cancelled.
        IEnumerable<Task<bool>> tasks = _connections.Select(async (connection, nodeIndex) =>
        {
            try
            {
                IDatabase database = connection.GetDatabase();
                return await database.LockReleaseAsync(lockKey, lockValue);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(LogEvents.NodeReleaseFailed, exception, "Error releasing lock on Redis node {NodeIndex}", nodeIndex);
                return false;
            }
        });

        return await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        foreach (var conn in _ownedConnections)
        {
            try
            {
                conn.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(LogEvents.OwnedConnectionDisposeFailed, ex, "Error disposing an owned Redis connection");
            }
        }
    }
}
