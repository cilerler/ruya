using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.DistributedLock.InMemory.Providers;

namespace Ruya.Services.DistributedLock.InMemory.Tests;

/// <summary>
/// Unit tests for lock key and value validation in InMemory provider.
/// </summary>
[TestClass]
public class LockValidationTests
{
    private InMemoryLockProvider _provider = null!;

    [TestInitialize]
    public void Setup()
    {
        _provider = new InMemoryLockProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _provider?.Dispose();
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

    [TestMethod]
    public void LockKeyTooLong_ShouldHaveClearErrorMessage()
    {
        // Arrange
        var longKey = new string('x', 300);

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _provider.AcquireLockAsync(longKey, "value", TimeSpan.FromMinutes(1))).Result;

        // Assert
        StringAssert.Contains(exception.Message, "255");
        StringAssert.Contains(exception.Message, "300");
        StringAssert.Contains(exception.Message, "maximum length");
    }

    [TestMethod]
    public void LockValueTooLong_ShouldHaveClearErrorMessage()
    {
        // Arrange
        var longValue = new string('x', 300);

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _provider.AcquireLockAsync("key", longValue, TimeSpan.FromMinutes(1))).Result;

        // Assert
        StringAssert.Contains(exception.Message, "256");
        StringAssert.Contains(exception.Message, "300");
        StringAssert.Contains(exception.Message, "maximum length");
    }

    [TestMethod]
    public async Task SpecialCharactersInKey_ShouldBeAllowed()
    {
        // Arrange
        var specialKey = "lock:with:colons-and-dashes_and_underscores";

        // Act
        var result = await _provider.AcquireLockAsync(specialKey, "value", TimeSpan.FromMinutes(1));

        // Assert
        Assert.IsTrue(result, "Special characters should be allowed in lock keys");
    }

    [TestMethod]
    public async Task UnicodeCharactersInKey_ShouldBeAllowed()
    {
        // Arrange
        var unicodeKey = "lock-测试-🔒";

        // Act
        var result = await _provider.AcquireLockAsync(unicodeKey, "value", TimeSpan.FromMinutes(1));

        // Assert
        Assert.IsTrue(result, "Unicode characters should be allowed in lock keys");
    }

    [TestMethod]
    public async Task GuidInLockValue_ShouldWork()
    {
        // Arrange
        var guidValue = Guid.NewGuid().ToString();

        // Act
        var result = await _provider.AcquireLockAsync("key", guidValue, TimeSpan.FromMinutes(1));

        // Assert
        Assert.IsTrue(result, "GUID should be valid lock value");
    }

    [TestMethod]
    public async Task LongButValidKey_ShouldWork()
    {
        // Arrange
        var longKey = $"my-app:tenant-12345:resource-abc:lock-{new string('x', 200)}";

        // Act
        var result = await _provider.AcquireLockAsync(longKey, "value", TimeSpan.FromMinutes(1));

        // Assert
        Assert.IsTrue(result, "Long but valid key should work");
    }
}
