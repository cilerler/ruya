using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;
using Ruya.Services.DistributedLock.Redis.Providers;

namespace Ruya.Services.DistributedLock.Redis.Tests;

/// <summary>
/// Unit tests for lock key and value validation in Redis provider.
/// </summary>
[TestClass]
public class LockValidationTests
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
    public async Task AcquireLockAsync_ShouldSucceed_WhenLockKeyAtMaxLength()
    {
        // Arrange
        var maxKey = new string('a', 255); // Exactly at 255 max
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        _mockDatabase
            .Setup(db => db.LockTakeAsync(maxKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await _provider.AcquireLockAsync(maxKey, lockValue, expiry);

        // Assert
        Assert.IsTrue(result, "Lock should be acquired with max length key");
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldSucceed_WhenLockValueAtMaxLength()
    {
        // Arrange
        var lockKey = "test-key";
        var maxValue = new string('a', 256); // Exactly at 256 max
        var expiry = TimeSpan.FromMinutes(1);

        _mockDatabase
            .Setup(db => db.LockTakeAsync(lockKey, It.IsAny<RedisValue>(), expiry, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await _provider.AcquireLockAsync(lockKey, maxValue, expiry);

        // Assert
        Assert.IsTrue(result, "Lock should be acquired with max length value");
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenLockKeyExceedsMaxLength()
    {
        // Arrange
        var longKey = new string('a', 256);
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.ExtendLockAsync(longKey, lockValue, expiry);
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenLockKeyExceedsMaxLength()
    {
        // Arrange
        var longKey = new string('a', 256);
        var lockValue = "test-value";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.ReleaseLockAsync(longKey, lockValue);
        });
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldThrow_WhenLockKeyExceedsMaxLength()
    {
        // Arrange
        var longKey = new string('a', 256);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.LockExistsAsync(longKey);
        });
    }

    [TestMethod]
    public void ValidationException_ShouldIncludeActualLength()
    {
        // Arrange
        var longKey = new string('a', 300);
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _provider.AcquireLockAsync(longKey, lockValue, expiry)).Result;

        Assert.IsTrue(exception.Message.Contains("255"), "Error message should contain max length");
        Assert.IsTrue(exception.Message.Contains("300"), "Error message should contain actual length");
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
    public async Task AcquireLockAsync_ShouldThrow_WhenLockKeyIsWhitespace()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.AcquireLockAsync("   ", "value", TimeSpan.FromMinutes(1));
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

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockValueIsWhitespace()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _provider.AcquireLockAsync("key", "   ", TimeSpan.FromMinutes(1));
        });
    }
}
