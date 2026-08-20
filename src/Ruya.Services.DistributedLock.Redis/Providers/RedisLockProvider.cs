using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Common;
using StackExchange.Redis;

namespace Ruya.Services.DistributedLock.Redis.Providers;

/// <summary>
/// Redis-based implementation of distributed lock provider.
/// Uses StackExchange.Redis for robust distributed locking.
/// </summary>
public sealed class RedisLockProvider : IDistributedLockProvider
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisLockProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisLockProvider"/> class.
    /// </summary>
    /// <param name="connectionMultiplexer">The Redis connection multiplexer.</param>
    /// <param name="logger">The logger instance.</param>
    /// <remarks>
    /// <para>
    /// The <paramref name="connectionMultiplexer"/> should be registered as a singleton
    /// in the dependency injection container, as recommended by StackExchange.Redis.
    /// This provider does NOT take ownership of the connection and will NOT dispose it.
    /// </para>
    /// <para>
    /// Example DI registration:
    /// <code>
    /// services.AddRedisDistributedLock();
    /// </code>
    /// </para>
    /// </remarks>
    public RedisLockProvider(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisLockProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(logger);

        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
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

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            bool acquired = await _database.LockTakeAsync(lockKey, lockValue, expiry);
            if (cancellationToken.IsCancellationRequested)
            {
                if (acquired)
                {
                    try
                    {
                        await _database.LockReleaseAsync(lockKey, lockValue);
                    }
                    catch (Exception cleanupException)
                    {
                        _logger.LogWarning(LogEvents.CancellationCleanupFailed, cleanupException, "Redis error cleaning up a lock after caller cancellation");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return acquired;
        }
        catch (RedisException ex)
        {
            _logger.LogError(LogEvents.AcquireFailed, ex, "Redis error acquiring lock for key: {LockKey}", lockKey);
            throw;
        }
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

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            bool extended = await _database.LockExtendAsync(lockKey, lockValue, expiry);
            if (!extended)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(LogEvents.ExtendFailed, ex, "Redis error extending lock for key: {LockKey}", lockKey);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseLockAsync(
        string lockKey,
        string lockValue,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            bool released = await _database.LockReleaseAsync(lockKey, lockValue);
            cancellationToken.ThrowIfCancellationRequested();
            return released;
        }
        catch (RedisException ex)
        {
            _logger.LogError(LogEvents.ReleaseFailed, ex, "Redis error releasing lock for key: {LockKey}", lockKey);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> LockExistsAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            bool exists = await _database.KeyExistsAsync(lockKey);
            cancellationToken.ThrowIfCancellationRequested();
            return exists;
        }
        catch (RedisException ex)
        {
            _logger.LogError(LogEvents.ExistsFailed, ex, "Redis error checking lock existence for key: {LockKey}", lockKey);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ForceReleaseLockAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            bool deleted = await _database.KeyDeleteAsync(lockKey);
            cancellationToken.ThrowIfCancellationRequested();
            return deleted;
        }
        catch (RedisException ex)
        {
            _logger.LogError(LogEvents.ForceReleaseFailed, ex, "Redis error force releasing lock for key: {LockKey}", lockKey);
            throw;
        }
    }

    /// <inheritdoc />
    public string GetProviderName() => nameof(Ruya.Services.DistributedLock.Redis);
}
