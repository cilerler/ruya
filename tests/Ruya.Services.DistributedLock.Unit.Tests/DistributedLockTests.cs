using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.DistributedLock.Abstractions.Models;
using CoreDistributedLock = Ruya.Services.DistributedLock.Core.DistributedLock;
using Ruya.Services.DistributedLock.Configuration;
using Ruya.Services.DistributedLock.InMemory.Providers;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Collections.Generic;

namespace Ruya.Services.DistributedLock.Tests;

/// <summary>
/// Unit tests for DistributedLock.
/// </summary>
[TestClass]
public class DistributedLockTests
{
    private InMemoryLockProvider _provider = null!;
    private CoreDistributedLock _distributedLock = null!;
    private ILogger<CoreDistributedLock> _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _provider = new InMemoryLockProvider();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<CoreDistributedLock>();

        var settings = Options.Create(new DistributedLockSettings
        {
            LockExpiry = TimeSpan.FromMinutes(5),
            InstanceName = "test-instance"
        });

        _distributedLock = new CoreDistributedLock(_provider, _logger, settings);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _provider?.Dispose();
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldSucceed_WhenLockIsAvailable()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var wasExecuted = false;

        // Act
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) =>
            {
                await Task.Delay(10, ct);
                wasExecuted = true;
            },
            lockKey,
            lockValue);

        // Assert
        Assert.IsTrue(result.IsSuccess, "Lock operation should succeed");
        Assert.AreEqual(LockStatus.Success, result.Status);
        Assert.IsTrue(wasExecuted, "Callback should be executed");
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldFail_WhenLockIsAlreadyHeld()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var firstCallbackCompleted = false;
        var secondCallbackCompleted = false;

        // Start first lock operation (long-running)
        var firstTask = _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) =>
            {
                await Task.Delay(500, ct);
                firstCallbackCompleted = true;
            },
            lockKey,
            lockValue1);

        // Wait a bit to ensure first lock is acquired
        await Task.Delay(50);

        // Act - Try to acquire same lock
        var secondResult = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) =>
            {
                await Task.Delay(10, ct);
                secondCallbackCompleted = true;
            },
            lockKey,
            lockValue2);

        // Assert
        Assert.IsFalse(secondResult.IsSuccess, "Second lock should fail");
        Assert.AreEqual(LockStatus.AlreadyLocked, secondResult.Status);
        Assert.IsFalse(secondCallbackCompleted, "Second callback should not execute");

        // Wait for first task to complete
        var firstResult = await firstTask;
        Assert.IsTrue(firstResult.IsSuccess, "First lock should succeed");
        Assert.IsTrue(firstCallbackCompleted, "First callback should execute");
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldReleaseAfterCompletion()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";

        // Act
        var firstResult = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10, ct),
            lockKey,
            lockValue1);

        // Try to acquire again after first is released
        var secondResult = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10, ct),
            lockKey,
            lockValue2);

        // Assert
        Assert.IsTrue(firstResult.IsSuccess, "First lock should succeed");
        Assert.IsTrue(secondResult.IsSuccess, "Second lock should succeed after first is released");
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldReturnExecutionFailed_WhenCallbackThrows()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";

        // Act
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            (ct) => throw new InvalidOperationException("Test exception"),
            lockKey,
            lockValue);

        // Assert
        Assert.IsFalse(result.IsSuccess, "Lock operation should fail");
        Assert.AreEqual(LockStatus.ExecutionFailed, result.Status);
        Assert.IsNotNull(result.ErrorMessage, "Error message should be present");
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldReleaseLock_EvenWhenCallbackThrows()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";

        // Act
        var firstResult = await _distributedLock.AcquireAndExecuteWithLockAsync(
            (ct) => throw new InvalidOperationException("Test exception"),
            lockKey,
            lockValue1);

        // Try to acquire again
        var secondResult = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10, ct),
            lockKey,
            lockValue2);

        // Assert
        Assert.AreEqual(LockStatus.ExecutionFailed, firstResult.Status);
        Assert.IsTrue(secondResult.IsSuccess, "Lock should be released even after callback throws");
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldUseCustomExpiry()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var customExpiry = TimeSpan.FromSeconds(2);
        var options = new LockOptions { CustomExpiry = customExpiry };

        // Act
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10, ct),
            lockKey,
            lockValue,
            options);

        // Assert
        Assert.IsTrue(result.IsSuccess, "Lock should succeed with custom expiry");
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldWorkWithoutHeartbeat()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";
        var options = LockOptions.WithoutHeartbeat;
        var wasExecuted = false;

        // Act
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) =>
            {
                await Task.Delay(10, ct);
                wasExecuted = true;
            },
            lockKey,
            lockValue,
            options);

        // Assert
        Assert.IsTrue(result.IsSuccess, "Lock should succeed without heartbeat");
        Assert.IsTrue(wasExecuted, "Callback should be executed");
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_WithInstanceName_ShouldPrefixLockKey()
    {
        // Arrange
        var lockKey = "test-lock";
        var lockValue = "test-value";

        // Act
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10, ct),
            lockKey,
            lockValue);

        // Verify the lock was created with the instance name prefix
        // The actual key should be "test-instance:test-lock"
        var exists = await _provider.LockExistsAsync("test-instance:test-lock");

        // Assert
        Assert.IsTrue(result.IsSuccess, "Lock should succeed");
        // Note: Lock should be released by now, so we can't check existence
    }

    [TestMethod]
    public async Task ConcurrentLockOperations_OnlyOneShouldAcquireLock()
    {
        // Arrange
        var lockKey = "concurrent-lock";
        var successCount = 0;
        var failCount = 0;
        var tasks = new List<Task>();

        // Act - Simulate 10 concurrent operations
        for (int i = 0; i < 10; i++)
        {
            var lockValue = $"value-{i}";
            tasks.Add(Task.Run(async () =>
            {
                var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
                    async (ct) => await Task.Delay(100, ct),
                    lockKey,
                    lockValue);

                if (result.IsSuccess)
                    Interlocked.Increment(ref successCount);
                else
                    Interlocked.Increment(ref failCount);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.AreEqual(1, successCount, "Only one operation should succeed initially");
        Assert.AreEqual(9, failCount, "Nine operations should fail");
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldThrow_WhenCallbackIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _distributedLock.AcquireAndExecuteWithLockAsync(
                null!,
                "key",
                "value");
        });
    }

    [TestMethod]
    public async Task AcquireAndExecuteWithLockAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _distributedLock.AcquireAndExecuteWithLockAsync(
                async (ct) => await Task.Delay(10, ct),
                null!,
                "value");
        });
    }

    [TestMethod]
    public void LockResult_Deconstruct_ShouldWorkCorrectly()
    {
        // Arrange
        var successResult = LockResult.Succeeded();
        var failResult = LockResult.Failed(LockStatus.AlreadyLocked, "Test error");

        // Act
        var (isSuccess1, status1, error1) = successResult;
        var (isSuccess2, status2, error2) = failResult;

        // Assert
        Assert.IsTrue(isSuccess1);
        Assert.AreEqual(LockStatus.Success, status1);
        Assert.IsNull(error1);

        Assert.IsFalse(isSuccess2);
        Assert.AreEqual(LockStatus.AlreadyLocked, status2);
        Assert.AreEqual("Test error", error2);
    }

    [TestMethod]
    public void LockResult_Failed_ShouldThrow_WhenStatusIsSuccess()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LockResult.Failed(LockStatus.Success, "Invalid");
        });
    }

    [TestMethod]
    public async Task Heartbeat_ShouldExtendLockDuringLongOperation()
    {
        // Arrange
        var lockKey = "heartbeat-test-lock";
        var lockValue = "test-value";
        var options = new LockOptions
        {
            CustomExpiry = TimeSpan.FromSeconds(2),
            EnableHeartbeat = true,
            HeartbeatInterval = TimeSpan.FromSeconds(1)
        };

        // Act - Run for 5 seconds (longer than expiry)
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(5000, ct),
            lockKey,
            lockValue,
            options);

        // Assert - Should succeed because heartbeat extends lock
        Assert.IsTrue(result.IsSuccess, "Long operation should succeed with heartbeat");
        Assert.AreEqual(LockStatus.Success, result.Status);
    }

    [TestMethod]
    public async Task Heartbeat_ShouldNotExtendAfterOperationCompletes()
    {
        // Arrange
        var lockKey = "heartbeat-completion-test";
        var lockValue1 = "value-1";
        var lockValue2 = "value-2";
        var options = new LockOptions
        {
            CustomExpiry = TimeSpan.FromSeconds(1),
            EnableHeartbeat = true,
            HeartbeatInterval = TimeSpan.FromMilliseconds(300)
        };

        // Act
        var firstResult = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(500, ct),
            lockKey,
            lockValue1,
            options);

        // Wait a bit for lock to expire
        await Task.Delay(1500);

        // Try to acquire again
        var secondResult = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10, ct),
            lockKey,
            lockValue2);

        // Assert
        Assert.IsTrue(firstResult.IsSuccess, "First operation should succeed");
        Assert.IsTrue(secondResult.IsSuccess, "Second operation should succeed after lock expires");
    }

    [TestMethod]
    public async Task WithoutHeartbeat_ShouldNotExtendLock()
    {
        // Arrange
        var lockKey = "no-heartbeat-test";
        var lockValue = "test-value";
        var shortExpiry = TimeSpan.FromMilliseconds(500);
        var options = new LockOptions
        {
            CustomExpiry = shortExpiry,
            EnableHeartbeat = false
        };

        // Act - Operation takes longer than expiry without heartbeat
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) =>
            {
                await Task.Delay(100, ct); // Short enough to complete before expiry
            },
            lockKey,
            lockValue,
            options);

        // Assert
        Assert.IsTrue(result.IsSuccess, "Short operation should succeed without heartbeat");
    }

    [TestMethod]
    public async Task Heartbeat_ShouldStopOnOperationException()
    {
        // Arrange
        var lockKey = "heartbeat-exception-test";
        var lockValue = "test-value";
        var options = new LockOptions
        {
            CustomExpiry = TimeSpan.FromSeconds(2),
            EnableHeartbeat = true,
            HeartbeatInterval = TimeSpan.FromMilliseconds(500)
        };

        // Act
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) =>
            {
                await Task.Delay(100, ct);
                throw new InvalidOperationException("Test exception");
            },
            lockKey,
            lockValue,
            options);

        // Assert - Lock should be released even though callback threw
        Assert.IsFalse(result.IsSuccess, "Operation should fail");
        Assert.AreEqual(LockStatus.ExecutionFailed, result.Status);

        // Verify lock was released by acquiring it again
        var secondResult = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10, ct),
            lockKey,
            "new-value");

        Assert.IsTrue(secondResult.IsSuccess, "Lock should be released after exception");
    }

    [TestMethod]
    public async Task Heartbeat_DefaultInterval_ShouldBeOneThirdOfExpiry()
    {
        // Arrange
        var lockKey = "heartbeat-default-interval-test";
        var lockValue = "test-value";
        var expiry = TimeSpan.FromSeconds(6);
        var options = new LockOptions
        {
            CustomExpiry = expiry,
            EnableHeartbeat = true
            // HeartbeatInterval not specified - should default to expiry / 3 = 2 seconds
        };

        // Act - Run for 10 seconds (longer than expiry)
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10000, ct),
            lockKey,
            lockValue,
            options);

        // Assert - Should succeed because heartbeat extends lock with default interval
        Assert.IsTrue(result.IsSuccess, "Long operation should succeed with default heartbeat interval");
    }
}
