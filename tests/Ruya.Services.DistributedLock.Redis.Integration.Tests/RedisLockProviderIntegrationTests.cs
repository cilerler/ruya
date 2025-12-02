using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.DistributedLock.Redis.Providers;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Ruya.Services.DistributedLock.Redis.Tests;

/// <summary>
/// Integration tests for RedisLockProvider using a real Redis instance via Testcontainers.
/// These tests verify the lock provider functionality against an actual Redis server.
/// </summary>
[TestClass]
public sealed class RedisLockProviderIntegrationTests
{
    private static RedisContainer? _redisContainer;
    private static IConnectionMultiplexer? _connectionMultiplexer;
    private static ILogger<RedisLockProvider>? _logger;
    private static RedisLockProvider? _provider;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        // Start a Redis container for integration testing
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        // Create initial connection
        await CreateConnectionAsync();
    }

    private static async Task CreateConnectionAsync()
    {
        // Get the current connection string from the container
        var connectionString = _redisContainer!.GetConnectionString();

        // Configure connection to allow reconnection after failures
        var configOptions = ConfigurationOptions.Parse(connectionString);
        configOptions.AllowAdmin = true;
        configOptions.AbortOnConnectFail = false; // Allow reconnection attempts
        configOptions.ConnectTimeout = 5000; // 5 seconds
        configOptions.ConnectRetry = 5; // Retry connection 5 times

        // Dispose old connection if exists
        if (_connectionMultiplexer != null)
        {
            await _connectionMultiplexer.CloseAsync();
            _connectionMultiplexer.Dispose();
        }

        _connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configOptions);

        // Recreate provider with new connection
        if (_logger != null)
        {
            _provider = new RedisLockProvider(_connectionMultiplexer, _logger);
        }
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        // Clean up resources
        if (_connectionMultiplexer != null)
        {
            await _connectionMultiplexer.CloseAsync();
            _connectionMultiplexer.Dispose();
        }

        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
        }
    }

    [TestInitialize]
    public void TestInitialize()
    {
        // Create logger once if not exists
        if (_logger == null)
        {
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RedisLockProvider>();
        }

        // Recreate provider with current connection (may have changed after container restart)
        _provider = new RedisLockProvider(_connectionMultiplexer!, _logger);

        // Clear all keys before each test
        var endpoints = _connectionMultiplexer!.GetEndPoints();
        var server = _connectionMultiplexer.GetServer(endpoints[0]);
        server.FlushDatabase();
    }

    #region Basic Lock Operations

    [TestMethod]
    public async Task AcquireLockAsync_WithAvailableLock_ShouldSucceed()
    {
        // Arrange
        var lockKey = "test-lock-acquire";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        var result = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);

        // Assert
        Assert.IsTrue(result, "Lock acquisition should succeed when lock is available");

        // Verify lock exists in Redis
        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsTrue(exists, "Lock should exist in Redis after acquisition");
    }

    [TestMethod]
    public async Task AcquireLockAsync_WhenLockAlreadyHeld_ShouldFail()
    {
        // Arrange
        var lockKey = "test-lock-held";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        var firstAcquire = await _provider!.AcquireLockAsync(lockKey, lockValue1, expiry);
        var secondAcquire = await _provider.AcquireLockAsync(lockKey, lockValue2, expiry);

        // Assert
        Assert.IsTrue(firstAcquire, "First lock acquisition should succeed");
        Assert.IsFalse(secondAcquire, "Second lock acquisition should fail when lock is already held");
    }

    [TestMethod]
    public async Task AcquireLockAsync_AfterLockExpires_ShouldSucceed()
    {
        // Arrange
        var lockKey = "test-lock-expiry";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var expiry = TimeSpan.FromSeconds(2);

        // Act
        var firstAcquire = await _provider!.AcquireLockAsync(lockKey, lockValue1, expiry);
        Assert.IsTrue(firstAcquire, "First lock acquisition should succeed");

        // Wait for lock to expire
        await Task.Delay(TimeSpan.FromSeconds(3));

        var secondAcquire = await _provider.AcquireLockAsync(lockKey, lockValue2, expiry);

        // Assert
        Assert.IsTrue(secondAcquire, "Lock acquisition should succeed after previous lock expires");
    }

    [TestMethod]
    public async Task ReleaseLockAsync_WithCorrectValue_ShouldSucceed()
    {
        // Arrange
        var lockKey = "test-lock-release";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(30);

        await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);

        // Act
        var result = await _provider.ReleaseLockAsync(lockKey, lockValue);

        // Assert
        Assert.IsTrue(result, "Lock release should succeed with correct value");

        // Verify lock no longer exists
        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should not exist after release");
    }

    [TestMethod]
    public async Task ReleaseLockAsync_WithIncorrectValue_ShouldFail()
    {
        // Arrange
        var lockKey = "test-lock-release-wrong";
        var correctValue = "correct-value";
        var incorrectValue = "incorrect-value";
        var expiry = TimeSpan.FromSeconds(30);

        await _provider!.AcquireLockAsync(lockKey, correctValue, expiry);

        // Act
        var result = await _provider.ReleaseLockAsync(lockKey, incorrectValue);

        // Assert
        Assert.IsFalse(result, "Lock release should fail with incorrect value");

        // Verify lock still exists
        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsTrue(exists, "Lock should still exist after failed release");
    }

    [TestMethod]
    public async Task ExtendLockAsync_WithCorrectValue_ShouldSucceed()
    {
        // Arrange
        var lockKey = "test-lock-extend";
        var lockValue = "test-value";
        var initialExpiry = TimeSpan.FromSeconds(5);
        var extendedExpiry = TimeSpan.FromSeconds(30);

        await _provider!.AcquireLockAsync(lockKey, lockValue, initialExpiry);

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, lockValue, extendedExpiry);

        // Assert
        Assert.IsTrue(result, "Lock extension should succeed with correct value");

        // Verify lock still exists after initial expiry would have passed
        await Task.Delay(TimeSpan.FromSeconds(6));
        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsTrue(exists, "Lock should still exist after extension");
    }

    [TestMethod]
    public async Task ExtendLockAsync_WithIncorrectValue_ShouldFail()
    {
        // Arrange
        var lockKey = "test-lock-extend-wrong";
        var correctValue = "correct-value";
        var incorrectValue = "incorrect-value";
        var expiry = TimeSpan.FromSeconds(30);

        await _provider!.AcquireLockAsync(lockKey, correctValue, expiry);

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, incorrectValue, expiry);

        // Assert
        Assert.IsFalse(result, "Lock extension should fail with incorrect value");
    }

    [TestMethod]
    public async Task LockExistsAsync_WhenLockExists_ShouldReturnTrue()
    {
        // Arrange
        var lockKey = "test-lock-exists";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(30);

        await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);

        // Act
        var result = await _provider.LockExistsAsync(lockKey);

        // Assert
        Assert.IsTrue(result, "LockExists should return true when lock exists");
    }

    [TestMethod]
    public async Task LockExistsAsync_WhenLockDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        var lockKey = "test-lock-not-exists";

        // Act
        var result = await _provider!.LockExistsAsync(lockKey);

        // Assert
        Assert.IsFalse(result, "LockExists should return false when lock does not exist");
    }

    #endregion

    #region Concurrency Tests

    [TestMethod]
    public async Task ConcurrentAcquire_OnlyOneInstanceShouldSucceed()
    {
        // Arrange
        var lockKey = "test-concurrent-acquire";
        var expiry = TimeSpan.FromSeconds(30);
        var concurrentAttempts = 10;
        var successCount = 0;

        // Act
        var tasks = Enumerable.Range(0, concurrentAttempts)
            .Select(i => Task.Run(async () =>
            {
                var lockValue = $"value-{i}";
                var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
                if (acquired)
                {
                    Interlocked.Increment(ref successCount);
                }
                return acquired;
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.AreEqual(1, successCount, "Only one concurrent lock acquisition should succeed");
    }

    [TestMethod]
    public async Task ConcurrentRelease_OnlyOwnerShouldSucceed()
    {
        // Arrange
        var lockKey = "test-concurrent-release";
        var ownerValue = "owner-value";
        var expiry = TimeSpan.FromSeconds(30);
        var concurrentAttempts = 10;

        await _provider!.AcquireLockAsync(lockKey, ownerValue, expiry);

        // Act
        var tasks = Enumerable.Range(0, concurrentAttempts)
            .Select(i => Task.Run(async () =>
            {
                var lockValue = i == 0 ? ownerValue : $"wrong-value-{i}";
                return await _provider!.ReleaseLockAsync(lockKey, lockValue);
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.AreEqual(1, results.Count(r => r), "Only the owner should successfully release the lock");
    }

    [TestMethod]
    public async Task MultipleLocks_ShouldBeIndependent()
    {
        // Arrange
        var lockKey1 = "test-multi-lock-1";
        var lockKey2 = "test-multi-lock-2";
        var lockKey3 = "test-multi-lock-3";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        var result1 = await _provider!.AcquireLockAsync(lockKey1, lockValue, expiry);
        var result2 = await _provider.AcquireLockAsync(lockKey2, lockValue, expiry);
        var result3 = await _provider.AcquireLockAsync(lockKey3, lockValue, expiry);

        // Assert
        Assert.IsTrue(result1, "Lock 1 should be acquired");
        Assert.IsTrue(result2, "Lock 2 should be acquired");
        Assert.IsTrue(result3, "Lock 3 should be acquired");

        // Verify all locks exist independently
        Assert.IsTrue(await _provider.LockExistsAsync(lockKey1));
        Assert.IsTrue(await _provider.LockExistsAsync(lockKey2));
        Assert.IsTrue(await _provider.LockExistsAsync(lockKey3));

        // Release one lock shouldn't affect others
        await _provider.ReleaseLockAsync(lockKey2, lockValue);

        Assert.IsTrue(await _provider.LockExistsAsync(lockKey1));
        Assert.IsFalse(await _provider.LockExistsAsync(lockKey2));
        Assert.IsTrue(await _provider.LockExistsAsync(lockKey3));
    }

    #endregion

    #region Lock Lifecycle Tests

    [TestMethod]
    public async Task FullLockLifecycle_ShouldWorkCorrectly()
    {
        // Arrange
        var lockKey = "test-lifecycle";
        var lockValue = "test-value";
        var initialExpiry = TimeSpan.FromSeconds(10);
        var extendedExpiry = TimeSpan.FromSeconds(20);

        // Act & Assert - Acquire
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, initialExpiry);
        Assert.IsTrue(acquired, "Lock should be acquired");
        Assert.IsTrue(await _provider.LockExistsAsync(lockKey), "Lock should exist after acquisition");

        // Act & Assert - Extend
        var extended = await _provider.ExtendLockAsync(lockKey, lockValue, extendedExpiry);
        Assert.IsTrue(extended, "Lock should be extended");
        Assert.IsTrue(await _provider.LockExistsAsync(lockKey), "Lock should still exist after extension");

        // Act & Assert - Release
        var released = await _provider.ReleaseLockAsync(lockKey, lockValue);
        Assert.IsTrue(released, "Lock should be released");
        Assert.IsFalse(await _provider.LockExistsAsync(lockKey), "Lock should not exist after release");

        // Act & Assert - Re-acquire
        var reacquired = await _provider.AcquireLockAsync(lockKey, lockValue, initialExpiry);
        Assert.IsTrue(reacquired, "Lock should be re-acquired after release");
    }

    [TestMethod]
    public async Task LockReacquisition_AfterRelease_ShouldSucceed()
    {
        // Arrange
        var lockKey = "test-reacquire";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        await _provider!.AcquireLockAsync(lockKey, lockValue1, expiry);
        await _provider.ReleaseLockAsync(lockKey, lockValue1);
        var reacquired = await _provider.AcquireLockAsync(lockKey, lockValue2, expiry);

        // Assert
        Assert.IsTrue(reacquired, "Different value should be able to acquire lock after release");
    }

    #endregion

    #region Stress Tests

    [TestMethod]
    public async Task StressTest_MultipleLocksRapidAcquireRelease_ShouldHandleCorrectly()
    {
        // Arrange
        var iterations = 100;
        var lockKey = "stress-test-lock";
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var lockValue = $"value-{i}";
            var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
            Assert.IsTrue(acquired, $"Lock acquisition {i} should succeed");

            var released = await _provider.ReleaseLockAsync(lockKey, lockValue);
            Assert.IsTrue(released, $"Lock release {i} should succeed");
        }

        // Assert
        var exists = await _provider!.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should not exist after all operations");
    }

    [TestMethod]
    public async Task StressTest_ParallelDifferentLocks_ShouldAllSucceed()
    {
        // Arrange
        var parallelLocks = 50;
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        var tasks = Enumerable.Range(0, parallelLocks)
            .Select(i => Task.Run(async () =>
            {
                var lockKey = $"parallel-lock-{i}";
                var lockValue = $"value-{i}";

                var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
                Assert.IsTrue(acquired, $"Lock {i} should be acquired");

                // Simulate some work
                await Task.Delay(TimeSpan.FromMilliseconds(10));

                var released = await _provider.ReleaseLockAsync(lockKey, lockValue);
                Assert.IsTrue(released, $"Lock {i} should be released");
            }))
            .ToArray();

        // Assert
        await Task.WhenAll(tasks);
    }

    [TestMethod]
    public async Task StressTest_HighContentionSingleLock_ShouldMaintainConsistency()
    {
        // Arrange
        var lockKey = "high-contention-lock";
        var expiry = TimeSpan.FromSeconds(30);
        var workers = 20;
        var attemptsPerWorker = 10;
        var totalSuccessfulAcquisitions = 0;

        // Act
        var tasks = Enumerable.Range(0, workers)
            .Select(workerId => Task.Run(async () =>
            {
                for (int attempt = 0; attempt < attemptsPerWorker; attempt++)
                {
                    var lockValue = $"worker-{workerId}-attempt-{attempt}";
                    var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);

                    if (acquired)
                    {
                        Interlocked.Increment(ref totalSuccessfulAcquisitions);

                        // Simulate work
                        await Task.Delay(TimeSpan.FromMilliseconds(5));

                        var released = await _provider.ReleaseLockAsync(lockKey, lockValue);
                        Assert.IsTrue(released, "Release should succeed for acquired lock");
                    }

                    // Small delay before retry
                    await Task.Delay(TimeSpan.FromMilliseconds(1));
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.IsTrue(totalSuccessfulAcquisitions > 0, "At least some acquisitions should succeed");
        Assert.IsTrue(totalSuccessfulAcquisitions <= workers * attemptsPerWorker,
            "Cannot have more successful acquisitions than total attempts");

        // Verify lock is released
        var exists = await _provider!.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should be released after all operations");
    }

    #endregion

    #region Expiration Tests

    [TestMethod]
    public async Task LockExpiration_ShouldAllowNewAcquisition()
    {
        // Arrange
        var lockKey = "test-expiration";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var shortExpiry = TimeSpan.FromSeconds(1);

        // Act
        var firstAcquire = await _provider!.AcquireLockAsync(lockKey, lockValue1, shortExpiry);
        Assert.IsTrue(firstAcquire);

        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(2));

        var secondAcquire = await _provider.AcquireLockAsync(lockKey, lockValue2, TimeSpan.FromSeconds(30));

        // Assert
        Assert.IsTrue(secondAcquire, "Should be able to acquire lock after expiration");
    }

    [TestMethod]
    public async Task ExtendLock_ShouldPreventExpiration()
    {
        // Arrange
        var lockKey = "test-extend-prevent-expiry";
        var lockValue = "test-value";
        var initialExpiry = TimeSpan.FromSeconds(3);
        var extensionExpiry = TimeSpan.FromSeconds(10);

        // Act
        await _provider!.AcquireLockAsync(lockKey, lockValue, initialExpiry);

        // Wait, then extend before expiration
        await Task.Delay(TimeSpan.FromSeconds(2));
        var extended = await _provider.ExtendLockAsync(lockKey, lockValue, extensionExpiry);
        Assert.IsTrue(extended);

        // Wait beyond initial expiry
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsTrue(exists, "Lock should still exist after extension");
    }

    [TestMethod]
    public async Task ExtendExpiredLock_ShouldFail()
    {
        // Arrange
        var lockKey = "test-extend-expired";
        var lockValue = "test-value";
        var shortExpiry = TimeSpan.FromSeconds(1);

        // Act
        await _provider!.AcquireLockAsync(lockKey, lockValue, shortExpiry);

        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(2));

        var extended = await _provider.ExtendLockAsync(lockKey, lockValue, TimeSpan.FromSeconds(30));

        // Assert
        Assert.IsFalse(extended, "Should not be able to extend an expired lock");
    }

    #endregion

    #region Cancellation Tests

    [TestMethod]
    public async Task AcquireLockAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var lockKey = "test-cancellation-acquire";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(30);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _provider!.AcquireLockAsync(lockKey, lockValue, expiry, cts.Token),
            "Should throw OperationCanceledException when token is cancelled");
    }

    [TestMethod]
    public async Task ReleaseLockAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var lockKey = "test-cancellation-release";
        var lockValue = "test-value";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _provider!.ReleaseLockAsync(lockKey, lockValue, cts.Token),
            "Should throw OperationCanceledException when token is cancelled");
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public async Task MaxLengthLockKey_ShouldWorkCorrectly()
    {
        // Arrange
        var lockKey = new string('x', 255); // Max allowed length
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
        var exists = await _provider.LockExistsAsync(lockKey);
        var released = await _provider.ReleaseLockAsync(lockKey, lockValue);

        // Assert
        Assert.IsTrue(acquired, "Should acquire lock with max length key");
        Assert.IsTrue(exists, "Lock should exist with max length key");
        Assert.IsTrue(released, "Should release lock with max length key");
    }

    [TestMethod]
    public async Task MaxLengthLockValue_ShouldWorkCorrectly()
    {
        // Arrange
        var lockKey = "test-max-value";
        var lockValue = new string('y', 256); // Max allowed length
        var expiry = TimeSpan.FromSeconds(30);

        // Act
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
        var released = await _provider.ReleaseLockAsync(lockKey, lockValue);

        // Assert
        Assert.IsTrue(acquired, "Should acquire lock with max length value");
        Assert.IsTrue(released, "Should release lock with max length value");
    }

    [TestMethod]
    public async Task VeryShortExpiry_ShouldWorkCorrectly()
    {
        // Arrange
        var lockKey = "test-short-expiry";
        var lockValue = "test-value";
        var veryShortExpiry = TimeSpan.FromMilliseconds(100);

        // Act
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, veryShortExpiry);

        // Assert
        Assert.IsTrue(acquired, "Should acquire lock with very short expiry");

        // Wait for expiration
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should expire quickly");
    }

    [TestMethod]
    public async Task LongExpiry_ShouldWorkCorrectly()
    {
        // Arrange
        var lockKey = "test-long-expiry";
        var lockValue = "test-value";
        var longExpiry = TimeSpan.FromHours(1);

        // Act
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, longExpiry);

        // Assert
        Assert.IsTrue(acquired, "Should acquire lock with long expiry");

        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsTrue(exists, "Lock should exist with long expiry");

        // Cleanup
        await _provider.ReleaseLockAsync(lockKey, lockValue);
    }

    #endregion

    #region Network Disconnect and Resilience Tests

    [TestMethod]
    [Timeout(30000)]
    public async Task AcquireLockAsync_WhenRedisDisconnected_ShouldThrowException()
    {
        // Arrange
        var lockKey = "test-disconnect-acquire";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(10);

        try
        {
            // Act - Stop Redis container to simulate network disconnect
            await _redisContainer!.StopAsync();

            // Wait a moment for the connection to detect the issue
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Assert - Should throw exception when Redis is disconnected
            await Assert.ThrowsAsync<RedisConnectionException>(
                async () => await _provider!.AcquireLockAsync(lockKey, lockValue, expiry),
                "Should throw exception when Redis is disconnected");
        }
        finally
        {
            // Cleanup - Always restart the container
            await _redisContainer!.StartAsync();
            await WaitForRedisReconnection();
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task ReleaseLockAsync_WhenRedisDisconnected_ShouldThrowException()
    {
        // Arrange
        var lockKey = "test-disconnect-release";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(10);

        // First acquire a lock while Redis is up
        await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);

        try
        {
            // Act - Stop Redis container to simulate network disconnect
            await _redisContainer!.StopAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Assert - Should throw exception when Redis is disconnected
            await Assert.ThrowsAsync<RedisConnectionException>(
                async () => await _provider!.ReleaseLockAsync(lockKey, lockValue),
                "Should throw exception when Redis is disconnected");
        }
        finally
        {
            // Cleanup
            await _redisContainer!.StartAsync();
            await WaitForRedisReconnection();
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task ExtendLockAsync_WhenRedisDisconnected_ShouldThrowException()
    {
        // Arrange
        var lockKey = "test-disconnect-extend";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(10);

        // First acquire a lock while Redis is up
        await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);

        try
        {
            // Act - Stop Redis container to simulate network disconnect
            await _redisContainer!.StopAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Assert - Should throw exception when Redis is disconnected
            await Assert.ThrowsAsync<RedisConnectionException>(
                async () => await _provider!.ExtendLockAsync(lockKey, lockValue, expiry),
                "Should throw exception when Redis is disconnected");
        }
        finally
        {
            // Cleanup
            await _redisContainer!.StartAsync();
            await WaitForRedisReconnection();
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task LockExistsAsync_WhenRedisDisconnected_ShouldThrowException()
    {
        // Arrange
        var lockKey = "test-disconnect-exists";

        try
        {
            // Act - Stop Redis container to simulate network disconnect
            await _redisContainer!.StopAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Assert - Should throw exception when Redis is disconnected
            await Assert.ThrowsAsync<RedisConnectionException>(
                async () => await _provider!.LockExistsAsync(lockKey),
                "Should throw exception when Redis is disconnected");
        }
        finally
        {
            // Cleanup
            await _redisContainer!.StartAsync();
            await WaitForRedisReconnection();
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task AcquireLockAsync_AfterRedisReconnects_ShouldSucceed()
    {
        // Arrange
        var lockKey = "test-reconnect-acquire";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(10);

        // Act - Stop Redis container
        await _redisContainer!.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Start Redis container
        await _redisContainer!.StartAsync();

        // Wait for reconnection
        await WaitForRedisReconnection();

        // Assert - Should be able to acquire lock after reconnection
        var result = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
        Assert.IsTrue(result, "Lock acquisition should succeed after Redis reconnects");
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task AllLockOperations_AfterRedisReconnects_ShouldWorkNormally()
    {
        // Arrange
        var lockKey = "test-full-lifecycle-reconnect";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(10);

        // Act - Simulate disconnect and reconnect
        await _redisContainer!.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        await _redisContainer!.StartAsync();
        await WaitForRedisReconnection();

        // Assert - Test full lock lifecycle after reconnection
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
        Assert.IsTrue(acquired, "Should acquire lock after reconnection");

        var exists = await _provider!.LockExistsAsync(lockKey);
        Assert.IsTrue(exists, "Lock should exist after reconnection");

        var extended = await _provider!.ExtendLockAsync(lockKey, lockValue, TimeSpan.FromSeconds(20));
        Assert.IsTrue(extended, "Should extend lock after reconnection");

        var released = await _provider!.ReleaseLockAsync(lockKey, lockValue);
        Assert.IsTrue(released, "Should release lock after reconnection");

        exists = await _provider!.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should not exist after release");
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task ConnectionMultiplexer_ShouldAutomaticallyReconnect_AfterNetworkRecovery()
    {
        // Arrange
        var lockKey = "test-auto-reconnect";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(10);

        // Verify connection is working
        Assert.IsTrue(_connectionMultiplexer!.IsConnected, "Connection should be connected initially");

        // Act - Stop container to simulate network disconnect
        await _redisContainer!.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Start container
        await _redisContainer!.StartAsync();

        // Wait for automatic reconnection with retry logic
        await WaitForRedisReconnection();

        // Assert - Connection should be restored and operations should work
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
        Assert.IsTrue(acquired, "Should acquire lock after automatic reconnection");

        var released = await _provider!.ReleaseLockAsync(lockKey, lockValue);
        Assert.IsTrue(released, "Should release lock after automatic reconnection");
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task LockState_ShouldPersist_AfterNetworkDisconnectAndReconnect()
    {
        // Arrange
        var lockKey = "test-persist-state";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(20);

        // Acquire lock with long expiry while Redis is up
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
        Assert.IsTrue(acquired, "Lock should be acquired initially");

        // Act - Stop Redis container for a short time (less than expiry)
        await _redisContainer!.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(3));
        await _redisContainer!.StartAsync();
        await WaitForRedisReconnection();

        // Assert - Lock should still exist after reconnection
        var exists = await _provider!.LockExistsAsync(lockKey);
        Assert.IsTrue(exists, "Lock should persist after network disconnect and reconnect");

        // Verify we can still release with the correct value
        var released = await _provider!.ReleaseLockAsync(lockKey, lockValue);
        Assert.IsTrue(released, "Should be able to release the persisted lock");
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task AcquiredLock_CanBeReleased_AfterNetworkRecovery()
    {
        // Arrange
        var lockKey = "test-release-after-recovery";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(20);

        // Acquire lock while Redis is up
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, expiry);
        Assert.IsTrue(acquired, "Lock should be acquired");

        // Act - Simulate network disconnect and reconnect
        await _redisContainer!.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        await _redisContainer!.StartAsync();
        await WaitForRedisReconnection();

        // Assert - Should be able to release the lock with correct value
        var released = await _provider!.ReleaseLockAsync(lockKey, lockValue);
        Assert.IsTrue(released, "Should release lock after network recovery");

        // Verify lock no longer exists
        var exists = await _provider!.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should not exist after release");
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task LockExpiry_ShouldContinueDuring_NetworkDisconnect()
    {
        // Arrange
        var lockKey = "test-expiry-during-disconnect";
        var lockValue = "test-value";
        var shortExpiry = TimeSpan.FromSeconds(3);

        // Acquire lock with short expiry
        var acquired = await _provider!.AcquireLockAsync(lockKey, lockValue, shortExpiry);
        Assert.IsTrue(acquired, "Lock should be acquired");

        // Act - Stop container for longer than expiry time
        await _redisContainer!.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));
        await _redisContainer!.StartAsync();
        await WaitForRedisReconnection();

        // Assert - Lock should have expired
        var exists = await _provider!.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should have expired during network disconnect");

        // Verify can acquire new lock with same key
        var newLockValue = "new-value";
        var reacquired = await _provider!.AcquireLockAsync(lockKey, newLockValue, TimeSpan.FromSeconds(10));
        Assert.IsTrue(reacquired, "Should be able to acquire new lock after previous one expired");
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task LockOperations_WithShortTimeout_ShouldFailFast_WhenRedisDisconnected()
    {
        // Arrange
        var lockKey = "test-timeout-failfast";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(5);

        // Create a connection with short timeout
        var configOptions = ConfigurationOptions.Parse(_redisContainer!.GetConnectionString());
        configOptions.ConnectTimeout = 1000; // 1 second
        configOptions.SyncTimeout = 1000; // 1 second
        configOptions.AllowAdmin = true;

        IConnectionMultiplexer? shortTimeoutConnection = null;
        RedisLockProvider? shortTimeoutProvider = null;

        try
        {
            shortTimeoutConnection = await ConnectionMultiplexer.ConnectAsync(configOptions);
            shortTimeoutProvider = new RedisLockProvider(shortTimeoutConnection, _logger!);

            // Stop Redis to simulate network disconnect
            await _redisContainer!.StopAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Act - Measure time for operation to fail
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await shortTimeoutProvider.AcquireLockAsync(lockKey, lockValue, expiry);
                Assert.Fail("Expected exception was not thrown");
            }
            catch (RedisConnectionException)
            {
                stopwatch.Stop();

                // Assert - Should fail within a reasonable time (less than 5 seconds)
                Assert.IsTrue(stopwatch.ElapsedMilliseconds < 5000,
                    $"Operation should fail fast, but took {stopwatch.ElapsedMilliseconds}ms");
            }
        }
        finally
        {
            // Cleanup
            await _redisContainer!.StartAsync();

            if (shortTimeoutConnection != null)
            {
                await shortTimeoutConnection.CloseAsync();
                shortTimeoutConnection.Dispose();
            }

            await WaitForRedisReconnection();
        }
    }

    /// <summary>
    /// Helper method to wait for Redis to reconnect after a network issue.
    /// Recreates the ConnectionMultiplexer with the new container endpoint.
    /// </summary>
    private static async Task WaitForRedisReconnection()
    {
        const int maxRetries = 5;
        const int delayMilliseconds = 1000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // Recreate connection with new container endpoint (port may have changed)
                await CreateConnectionAsync();

                // Verify connection works
                var db = _connectionMultiplexer!.GetDatabase();
                await db.PingAsync();
                return; // Successfully reconnected
            }
            catch
            {
                if (i == maxRetries - 1)
                {
                    throw; // Rethrow on final attempt
                }
                await Task.Delay(delayMilliseconds);
            }
        }
    }

    #endregion
}
