using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.DistributedLock.Redis.Providers;
using StackExchange.Redis;

namespace Ruya.Services.DistributedLock.Redis.Tests;

/// <summary>
/// Unit tests for RedisLockProvider.
/// </summary>
[TestClass]
public class RedisLockProviderTests
{
    private Mock<IConnectionMultiplexer> _mockConnectionMultiplexer = null!;
    private Mock<IDatabase> _mockDatabase = null!;
    private Mock<ILogger<RedisLockProvider>> _mockLogger = null!;
    private RedisLockProvider _provider = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockConnectionMultiplexer = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<RedisLockProvider>>();

        _mockConnectionMultiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(_mockDatabase.Object);

        _provider = new RedisLockProvider(_mockConnectionMultiplexer.Object, _mockLogger.Object);
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_ShouldThrow_WhenConnectionMultiplexerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new RedisLockProvider(null!, _mockLogger.Object);
        });
    }

    [TestMethod]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new RedisLockProvider(_mockConnectionMultiplexer.Object, null!);
        });
    }

    #endregion

    #region AcquireLockAsync Tests

    [TestMethod]
    public async Task AcquireLockAsync_ShouldSucceed_WhenLockIsAvailable()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        _mockDatabase
            .Setup(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await _provider.AcquireLockAsync(lockKey, lockValue, expiry);

        // Assert
        Assert.IsTrue(result, "Lock should be acquired successfully");
        _mockDatabase.Verify(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None), Times.Once);
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldFail_WhenLockIsAlreadyHeld()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        _mockDatabase
            .Setup(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await _provider.AcquireLockAsync(lockKey, lockValue, expiry);

        // Assert
        Assert.IsFalse(result, "Lock acquisition should fail when already held");
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.AcquireLockAsync(null!, "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockValueIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.AcquireLockAsync("key", null!, TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenCancellationRequested()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _provider.AcquireLockAsync("key", "value", TimeSpan.FromMinutes(1), cts.Token);
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrowAndLog_WhenRedisExceptionOccurs()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        _mockDatabase
            .Setup(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ThrowsAsync(new RedisException("Redis connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisException>(async () =>
        {
            await _provider.AcquireLockAsync(lockKey, lockValue, expiry);
        });

        // Verify logging occurred
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(lockKey)),
                It.IsAny<RedisException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ExtendLockAsync Tests

    [TestMethod]
    public async Task ExtendLockAsync_ShouldSucceed_WhenLockIsHeldByCorrectValue()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(2);

        _mockDatabase
            .Setup(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, lockValue, expiry);

        // Assert
        Assert.IsTrue(result, "Lock extension should succeed");
        _mockDatabase.Verify(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None), Times.Once);
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldFail_WhenLockValueDoesNotMatch()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(2);

        _mockDatabase
            .Setup(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, lockValue, expiry);

        // Assert
        Assert.IsFalse(result, "Lock extension should fail with wrong value");
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.ExtendLockAsync(null!, "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenLockValueIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.ExtendLockAsync("key", null!, TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenCancellationRequested()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _provider.ExtendLockAsync("key", "value", TimeSpan.FromMinutes(1), cts.Token);
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrowAndLog_WhenRedisExceptionOccurs()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(2);

        _mockDatabase
            .Setup(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ThrowsAsync(new RedisException("Redis connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisException>(async () =>
        {
            await _provider.ExtendLockAsync(lockKey, lockValue, expiry);
        });

        // Verify logging occurred
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(lockKey)),
                It.IsAny<RedisException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ReleaseLockAsync Tests

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldSucceed_WhenLockIsHeldByCorrectValue()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";

        _mockDatabase
            .Setup(db => db.LockReleaseAsync(lockKey, It.IsAny<RedisValue>(), CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await _provider.ReleaseLockAsync(lockKey, lockValue);

        // Assert
        Assert.IsTrue(result, "Lock release should succeed");
        _mockDatabase.Verify(db => db.LockReleaseAsync(lockKey, It.IsAny<RedisValue>(), CommandFlags.None), Times.Once);
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldFail_WhenLockValueDoesNotMatch()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";

        _mockDatabase
            .Setup(db => db.LockReleaseAsync(lockKey, It.IsAny<RedisValue>(), CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await _provider.ReleaseLockAsync(lockKey, lockValue);

        // Assert
        Assert.IsFalse(result, "Lock release should fail with wrong value");
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.ReleaseLockAsync(null!, "value");
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenLockValueIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.ReleaseLockAsync("key", null!);
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenCancellationRequested()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _provider.ReleaseLockAsync("key", "value", cts.Token);
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrowAndLog_WhenRedisExceptionOccurs()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";

        _mockDatabase
            .Setup(db => db.LockReleaseAsync(lockKey, It.IsAny<RedisValue>(), CommandFlags.None))
            .ThrowsAsync(new RedisException("Redis connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisException>(async () =>
        {
            await _provider.ReleaseLockAsync(lockKey, lockValue);
        });

        // Verify logging occurred
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(lockKey)),
                It.IsAny<RedisException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region LockExistsAsync Tests

    [TestMethod]
    public async Task LockExistsAsync_ShouldReturnTrue_WhenLockExists()
    {
        // Arrange
        var lockKey = "test-lock";

        _mockDatabase
            .Setup(db => db.KeyExistsAsync(lockKey, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var exists = await _provider.LockExistsAsync(lockKey);

        // Assert
        Assert.IsTrue(exists, "Lock should exist");
        _mockDatabase.Verify(db => db.KeyExistsAsync(lockKey, CommandFlags.None), Times.Once);
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldReturnFalse_WhenLockDoesNotExist()
    {
        // Arrange
        var lockKey = "non-existent-lock";

        _mockDatabase
            .Setup(db => db.KeyExistsAsync(lockKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var exists = await _provider.LockExistsAsync(lockKey);

        // Assert
        Assert.IsFalse(exists, "Lock should not exist");
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.LockExistsAsync(null!);
        });
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldThrow_WhenCancellationRequested()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _provider.LockExistsAsync("key", cts.Token);
        });
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldThrowAndLog_WhenRedisExceptionOccurs()
    {
        // Arrange
        var lockKey = "test-lock";

        _mockDatabase
            .Setup(db => db.KeyExistsAsync(lockKey, CommandFlags.None))
            .ThrowsAsync(new RedisException("Redis connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisException>(async () =>
        {
            await _provider.LockExistsAsync(lockKey);
        });

        // Verify logging occurred
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(lockKey)),
                It.IsAny<RedisException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Validation Tests

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockKeyExceedsMaxLength()
    {
        // Arrange
        var longKey = new string('a', 256); // Exceeds 255 max
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.AcquireLockAsync(longKey, lockValue, expiry);
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockValueExceedsMaxLength()
    {
        // Arrange
        var lockKey = "test-key";
        var longValue = new string('a', 257); // Exceeds 256 max
        var expiry = TimeSpan.FromMinutes(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.AcquireLockAsync(lockKey, longValue, expiry);
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockKeyIsEmpty()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.AcquireLockAsync("", "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockValueIsEmpty()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.AcquireLockAsync("key", "", TimeSpan.FromMinutes(1));
        });
    }

    #endregion

    #region Expiration Tests

    [TestMethod]
    public async Task AcquireLockAsync_ShouldSucceed_WhenPreviousLockExpired()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var expiry = TimeSpan.FromMilliseconds(100);

        // Simulate first lock acquisition succeeds
        _mockDatabase
            .Setup(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(true);

        // First acquisition
        var firstAcquire = await _provider.AcquireLockAsync(lockKey, lockValue1, expiry);

        // Simulate lock expired - second acquisition should succeed
        var secondAcquire = await _provider.AcquireLockAsync(lockKey, lockValue2, expiry);

        // Assert
        Assert.IsTrue(firstAcquire, "First lock acquisition should succeed");
        Assert.IsTrue(secondAcquire, "Second lock acquisition should succeed after expiry");
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldFail_WhenLockExpired()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        // Simulate lock expired - extend should fail
        _mockDatabase
            .Setup(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, lockValue, expiry);

        // Assert
        Assert.IsFalse(result, "Lock extension should fail when lock is expired");
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldReturnFalse_WhenLockExpired()
    {
        // Arrange
        var lockKey = "test-lock";

        // Simulate expired lock doesn't exist
        _mockDatabase
            .Setup(db => db.KeyExistsAsync(lockKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var exists = await _provider.LockExistsAsync(lockKey);

        // Assert
        Assert.IsFalse(exists, "Expired lock should not exist");
    }

    #endregion

    #region Concurrency Tests

    [TestMethod]
    public async Task ConcurrentAcquire_OnlyOneInstanceShouldSucceed()
    {
        // Arrange
        var lockKey = "concurrent-lock";
        var expiry = TimeSpan.FromMinutes(1);
        var acquiredCount = 0;

        // Simulate Redis behavior - only first acquire succeeds
        _mockDatabase
            .Setup(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(() =>
            {
                if (Interlocked.CompareExchange(ref acquiredCount, 1, 0) == 0)
                    return true;
                return false;
            });

        var tasks = new List<Task<bool>>();

        // Act - Simulate 10 concurrent lock acquisition attempts
        for (int i = 0; i < 10; i++)
        {
            var lockValue = $"value-{i}";
            tasks.Add(_provider.AcquireLockAsync(lockKey, lockValue, expiry));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r);
        Assert.AreEqual(1, successCount, "Only one concurrent acquisition should succeed");
    }

    [TestMethod]
    public async Task ConcurrentExtend_ShouldBeThreadSafe()
    {
        // Arrange
        var lockKey = "concurrent-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(2);

        _mockDatabase
            .Setup(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(true);

        var tasks = new List<Task<bool>>();

        // Act - Simulate 10 concurrent lock extension attempts
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_provider.ExtendLockAsync(lockKey, lockValue, expiry));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.IsTrue(results.All(r => r), "All concurrent extensions should succeed with correct value");
        _mockDatabase.Verify(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None), Times.Exactly(10));
    }

    [TestMethod]
    public async Task ConcurrentRelease_ShouldBeThreadSafe()
    {
        // Arrange
        var lockKey = "concurrent-lock";
        var lockValue = "test-value";
        var releaseCount = 0;

        // Simulate Redis behavior - only first release succeeds
        _mockDatabase
            .Setup(db => db.LockReleaseAsync(lockKey, It.IsAny<RedisValue>(), CommandFlags.None))
            .ReturnsAsync(() =>
            {
                if (Interlocked.CompareExchange(ref releaseCount, 1, 0) == 0)
                    return true;
                return false;
            });

        var tasks = new List<Task<bool>>();

        // Act - Simulate 10 concurrent lock release attempts
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_provider.ReleaseLockAsync(lockKey, lockValue));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r);
        Assert.AreEqual(1, successCount, "Only one concurrent release should succeed");
    }

    #endregion

    #region Integration Verification Tests

    [TestMethod]
    public async Task Provider_ShouldCallRedisMethodsWithCorrectParameters()
    {
        // Arrange
        var lockKey = "integration-lock";
        var lockValue = "integration-value";
        var expiry = TimeSpan.FromMinutes(5);

        _mockDatabase
            .Setup(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(true);

        _mockDatabase
            .Setup(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(true);

        _mockDatabase
            .Setup(db => db.LockReleaseAsync(lockKey, It.IsAny<RedisValue>(), CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await _provider.AcquireLockAsync(lockKey, lockValue, expiry);
        await _provider.ExtendLockAsync(lockKey, lockValue, expiry);
        await _provider.ReleaseLockAsync(lockKey, lockValue);

        // Assert - Verify all methods were called with correct parameters
        _mockDatabase.Verify(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None), Times.Once);
        _mockDatabase.Verify(db => db.LockExtendAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None), Times.Once);
        _mockDatabase.Verify(db => db.LockReleaseAsync(lockKey, It.IsAny<RedisValue>(), CommandFlags.None), Times.Once);
    }

    #endregion
}
