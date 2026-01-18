using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.DistributedLock.InMemory.Providers;

namespace Ruya.Services.DistributedLock.InMemory.Tests;

/// <summary>
/// Unit tests for InMemoryLockProvider.
/// </summary>
[TestClass]
public class InMemoryLockProviderTests
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
    public async Task AcquireLockAsync_ShouldSucceed_WhenLockIsAvailable()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        // Act
        var result = await _provider.AcquireLockAsync(lockKey, lockValue, expiry);

        // Assert
        Assert.IsTrue(result, "Lock should be acquired successfully");
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldFail_WhenLockIsAlreadyHeld()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var expiry = TimeSpan.FromMinutes(1);

        // Act
        var firstAcquire = await _provider.AcquireLockAsync(lockKey, lockValue1, expiry);
        var secondAcquire = await _provider.AcquireLockAsync(lockKey, lockValue2, expiry);

        // Assert
        Assert.IsTrue(firstAcquire, "First lock acquisition should succeed");
        Assert.IsFalse(secondAcquire, "Second lock acquisition should fail");
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldSucceed_WhenPreviousLockExpired()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var expiry = TimeSpan.FromMilliseconds(100);

        // Act
        var firstAcquire = await _provider.AcquireLockAsync(lockKey, lockValue1, expiry);
        await Task.Delay(200); // Wait for lock to expire
        var secondAcquire = await _provider.AcquireLockAsync(lockKey, lockValue2, expiry);

        // Assert
        Assert.IsTrue(firstAcquire, "First lock acquisition should succeed");
        Assert.IsTrue(secondAcquire, "Second lock acquisition should succeed after expiry");
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldSucceed_WhenLockIsHeldByCorrectValue()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        await _provider.AcquireLockAsync(lockKey, lockValue, expiry);

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, lockValue, TimeSpan.FromMinutes(2));

        // Assert
        Assert.IsTrue(result, "Lock extension should succeed");
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldFail_WhenLockValueDoesNotMatch()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var expiry = TimeSpan.FromMinutes(1);

        await _provider.AcquireLockAsync(lockKey, lockValue1, expiry);

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, lockValue2, TimeSpan.FromMinutes(2));

        // Assert
        Assert.IsFalse(result, "Lock extension should fail with wrong value");
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldFail_WhenLockDoesNotExist()
    {
        // Arrange
        var lockKey = "non-existent-lock";
        var lockValue = "test-value";

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, lockValue, TimeSpan.FromMinutes(1));

        // Assert
        Assert.IsFalse(result, "Lock extension should fail when lock doesn't exist");
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldSucceed_WhenLockIsHeldByCorrectValue()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        await _provider.AcquireLockAsync(lockKey, lockValue, expiry);

        // Act
        var result = await _provider.ReleaseLockAsync(lockKey, lockValue);

        // Assert
        Assert.IsTrue(result, "Lock release should succeed");

        // Verify lock is released by acquiring it again
        var reacquire = await _provider.AcquireLockAsync(lockKey, "new-value", expiry);
        Assert.IsTrue(reacquire, "Lock should be available after release");
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldFail_WhenLockValueDoesNotMatch()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var expiry = TimeSpan.FromMinutes(1);

        await _provider.AcquireLockAsync(lockKey, lockValue1, expiry);

        // Act
        var result = await _provider.ReleaseLockAsync(lockKey, lockValue2);

        // Assert
        Assert.IsFalse(result, "Lock release should fail with wrong value");
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldReturnTrue_WhenLockExists()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMinutes(1);

        await _provider.AcquireLockAsync(lockKey, lockValue, expiry);

        // Act
        var exists = await _provider.LockExistsAsync(lockKey);

        // Assert
        Assert.IsTrue(exists, "Lock should exist");
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldReturnFalse_WhenLockDoesNotExist()
    {
        // Arrange
        var lockKey = "non-existent-lock";

        // Act
        var exists = await _provider.LockExistsAsync(lockKey);

        // Assert
        Assert.IsFalse(exists, "Lock should not exist");
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldReturnFalse_WhenLockExpired()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMilliseconds(100);

        await _provider.AcquireLockAsync(lockKey, lockValue, expiry);
        await Task.Delay(200); // Wait for lock to expire

        // Act
        var exists = await _provider.LockExistsAsync(lockKey);

        // Assert
        Assert.IsFalse(exists, "Expired lock should not exist");
    }

    [TestMethod]
    public async Task ConcurrentAcquire_OnlyOneInstanceShouldSucceed()
    {
        // Arrange
        var lockKey = "concurrent-lock";
        var expiry = TimeSpan.FromMinutes(1);
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
    public async Task ClearAll_ShouldRemoveAllLocks()
    {
        // Arrange
        await _provider.AcquireLockAsync("lock-1", "value-1", TimeSpan.FromMinutes(1));
        await _provider.AcquireLockAsync("lock-2", "value-2", TimeSpan.FromMinutes(1));
        await _provider.AcquireLockAsync("lock-3", "value-3", TimeSpan.FromMinutes(1));

        // Act
        _provider.ClearAll();

        // Assert
        Assert.AreEqual(0, _provider.LockCount, "All locks should be cleared");

        var exists1 = await _provider.LockExistsAsync("lock-1");
        var exists2 = await _provider.LockExistsAsync("lock-2");
        var exists3 = await _provider.LockExistsAsync("lock-3");

        Assert.IsFalse(exists1, "Lock 1 should not exist");
        Assert.IsFalse(exists2, "Lock 2 should not exist");
        Assert.IsFalse(exists3, "Lock 3 should not exist");
    }

    [TestMethod]
    public async Task LockCount_ShouldReflectActiveLocks()
    {
        // Arrange & Act
        Assert.AreEqual(0, _provider.LockCount, "Initial count should be 0");

        await _provider.AcquireLockAsync("lock-1", "value-1", TimeSpan.FromMinutes(1));
        Assert.AreEqual(1, _provider.LockCount, "Count should be 1 after first lock");

        await _provider.AcquireLockAsync("lock-2", "value-2", TimeSpan.FromMinutes(1));
        Assert.AreEqual(2, _provider.LockCount, "Count should be 2 after second lock");

        await _provider.ReleaseLockAsync("lock-1", "value-1");
        Assert.AreEqual(1, _provider.LockCount, "Count should be 1 after releasing first lock");
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
    public async Task AcquireLockAsync_ShouldThrow_WhenProviderIsDisposed()
    {
        // Arrange
        _provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await _provider.AcquireLockAsync("key", "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldFail_WhenLockExpired()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMilliseconds(100);

        await _provider.AcquireLockAsync(lockKey, lockValue, expiry);
        await Task.Delay(200); // Wait for lock to expire

        // Act
        var result = await _provider.ExtendLockAsync(lockKey, lockValue, TimeSpan.FromMinutes(1));

        // Assert
        Assert.IsFalse(result, "Lock extension should fail when lock is expired");
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldFail_WhenLockDoesNotExist()
    {
        // Arrange
        var lockKey = "non-existent-lock";
        var lockValue = "test-value";

        // Act
        var result = await _provider.ReleaseLockAsync(lockKey, lockValue);

        // Assert
        Assert.IsFalse(result, "Lock release should fail when lock doesn't exist");
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
    public async Task LockExistsAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.LockExistsAsync(null!);
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenProviderIsDisposed()
    {
        // Arrange
        _provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await _provider.ExtendLockAsync("key", "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenProviderIsDisposed()
    {
        // Arrange
        _provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await _provider.ReleaseLockAsync("key", "value");
        });
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldThrow_WhenProviderIsDisposed()
    {
        // Arrange
        _provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await _provider.LockExistsAsync("key");
        });
    }

    [TestMethod]
    public void ClearAll_ShouldThrow_WhenProviderIsDisposed()
    {
        // Arrange
        _provider.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _provider.ClearAll();
        });
    }

    [TestMethod]
    public void Dispose_ShouldBeIdempotent()
    {
        // Act - Dispose twice
        _provider.Dispose();
        _provider.Dispose();

        // Assert - No exception should be thrown
        Assert.IsTrue(true, "Double dispose should not throw");
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
    public async Task CleanupTimer_ShouldRemoveExpiredLocks()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromMilliseconds(100);

        await _provider.AcquireLockAsync(lockKey, lockValue, expiry);
        Assert.AreEqual(1, _provider.LockCount, "Lock should be acquired");

        // Act - Wait for cleanup timer to run (runs every 10 seconds, but lock expires in 100ms)
        // We'll wait 15 seconds to ensure timer has run at least once
        await Task.Delay(15000);

        // Assert
        Assert.AreEqual(0, _provider.LockCount, "Expired lock should be cleaned up by timer");
        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should not exist after cleanup");
    }

    [TestMethod]
    public async Task ConcurrentExtend_ShouldBeThreadSafe()
    {
        // Arrange
        var lockKey = "concurrent-lock";
        var lockValue = "test-value";
        await _provider.AcquireLockAsync(lockKey, lockValue, TimeSpan.FromMinutes(1));

        var tasks = new List<Task<bool>>();

        // Act - Simulate 10 concurrent lock extension attempts
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_provider.ExtendLockAsync(lockKey, lockValue, TimeSpan.FromMinutes(2)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.IsTrue(results.All(r => r), "All concurrent extensions should succeed with correct value");
    }

    [TestMethod]
    public async Task ConcurrentRelease_ShouldBeThreadSafe()
    {
        // Arrange
        var lockKey = "concurrent-lock";
        var lockValue = "test-value";
        await _provider.AcquireLockAsync(lockKey, lockValue, TimeSpan.FromMinutes(1));

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
    [TestMethod]
    public async Task ForceReleaseLockAsync_ShouldSucceed_WhenLockExists()
    {
        // Arrange
        var lockKey = "force-release-lock";
        var lockValue = "test-value";
        await _provider.AcquireLockAsync(lockKey, lockValue, TimeSpan.FromMinutes(1));

        // Act
        var result = await _provider.ForceReleaseLockAsync(lockKey);

        // Assert
        Assert.IsTrue(result, "Force release should succeed when lock exists");
        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should not exist after force release");
    }

    [TestMethod]
    public async Task ForceReleaseLockAsync_ShouldReturnFalse_WhenLockDoesNotExist()
    {
        // Arrange
        var lockKey = "non-existent-lock";

        // Act
        var result = await _provider.ForceReleaseLockAsync(lockKey);

        // Assert
        Assert.IsFalse(result, "Force release should return false when lock doesn't exist");
    }

    [TestMethod]
    public async Task ForceReleaseLockAsync_ShouldSucceed_WhenLockExpired()
    {
        // Arrange
        var lockKey = "expired-lock";
        var lockValue = "test-value";
        await _provider.AcquireLockAsync(lockKey, lockValue, TimeSpan.FromMilliseconds(50));
        await Task.Delay(100); // Wait for expiration

        // Act
        var result = await _provider.ForceReleaseLockAsync(lockKey);

        // Assert
        // Depending on implementation, force releasing an expired lock might return true (removed) or false (was already effectively gone)
        // In InMemoryLockProvider, we typically remove it if it's there.
        // If the cleanup timer hasn't run yet, it's still in the dictionary.
        // If it WAS there, TryRemove returns true.
        // Given the short delay, cleanup timer (10s) probably hasn't run.
        Assert.IsTrue(result, "Force release should succeed (remove entry) even if expired but not yet cleaned up");
        
        var exists = await _provider.LockExistsAsync(lockKey);
        Assert.IsFalse(exists, "Lock should not exist after force release");
    }

    [TestMethod]
    public async Task ForceReleaseLockAsync_ShouldThrow_WhenLockKeyIsNull()
    {
         // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _provider.ForceReleaseLockAsync(null!);
        });
    }

    [TestMethod]
    public async Task ForceReleaseLockAsync_ShouldThrow_WhenProviderIsDisposed()
    {
        // Arrange
        _provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await _provider.ForceReleaseLockAsync("key");
        });
    }
}
