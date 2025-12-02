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
    /// services.AddSingleton&lt;IConnectionMultiplexer&gt;(sp =>
    ///     ConnectionMultiplexer.Connect("localhost:6379"));
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

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _database.LockTakeAsync(lockKey, lockValue, expiry);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error acquiring lock for key: {LockKey}", lockKey);
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

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _database.LockExtendAsync(lockKey, lockValue, expiry);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error extending lock for key: {LockKey}", lockKey);
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
            return await _database.LockReleaseAsync(lockKey, lockValue);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error releasing lock for key: {LockKey}", lockKey);
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
            return await _database.KeyExistsAsync(lockKey);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error checking lock existence for key: {LockKey}", lockKey);
            throw;
        }
    }

    /// <inheritdoc />
    public string GetProviderName() => "Redis";
}
