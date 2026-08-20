using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Abstractions.Models;
using Ruya.Services.DistributedLock.Configuration;
using CoreDistributedLock = Ruya.Services.DistributedLock.Core.DistributedLock;

namespace Ruya.Services.DistributedLock.Tests;

[TestClass]
public class DistributedLockSafetyTests
{
    private Mock<IDistributedLockProvider> _mockProvider = null!;
    private Mock<ILogger<CoreDistributedLock>> _mockLogger = null!;
    private IOptions<DistributedLockSettings> _settings = null!;
    private CoreDistributedLock _distributedLock = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<CoreDistributedLock>>();
        _settings = Options.Create(new DistributedLockSettings
        {
            LockExpiry = TimeSpan.FromSeconds(5),
            InstanceName = "test-instance"
        });

        _distributedLock = new CoreDistributedLock(_mockProvider.Object, _mockLogger.Object, _settings);
    }

    [TestMethod]
    public async Task AcquireAndExecute_ShouldCancelOperation_WhenHeartbeatFails()
    {
        // Arrange
        var lockKey = "safety-test";
        var lockValue = "safety-value";
        var options = new LockOptions
        {
            CustomExpiry = TimeSpan.FromSeconds(1),
            HeartbeatInterval = TimeSpan.FromMilliseconds(100)
        };

        // Setup provider to succeed initially, then fail extension
        _mockProvider.Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockProvider.Setup(p => p.ExtendLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Simulate lost lock

        _mockProvider.Setup(p => p.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        LockResult result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) =>
            {
                await Task.Delay(500, ct);
            },
            lockKey,
            lockValue,
            options);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(LockStatus.ExecutionFailed, result.Status);
        _mockProvider.Verify(p => p.ExtendLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenProviderHeartbeatTimesOut_CancelsProtectedOperation()
    {
        _mockProvider
            .Setup(p => p.AcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProvider
            .Setup(p => p.ExtendLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("provider heartbeat timed out"));
        _mockProvider
            .Setup(p => p.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                CancellationToken.None))
            .ReturnsAsync(true);

        Task<LockResult> operation = _distributedLock.AcquireAndExecuteWithLockAsync(
            callbackToken => Task.Delay(Timeout.InfiniteTimeSpan, callbackToken),
            "key",
            "value",
            new LockOptions
            {
                CustomExpiry = TimeSpan.FromSeconds(1),
                HeartbeatInterval = TimeSpan.FromMilliseconds(10)
            });

        LockResult result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(LockStatus.ExecutionFailed, result.Status);
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenCallbackIgnoresCancellation_DoesNotReportSuccessAfterHeartbeatLoss()
    {
        _mockProvider
            .Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProvider
            .Setup(p => p.ExtendLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockProvider
            .Setup(p => p.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(true);

        LockResult result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            _ => Task.Delay(TimeSpan.FromMilliseconds(75), CancellationToken.None),
            "key",
            "value",
            new LockOptions
            {
                CustomExpiry = TimeSpan.FromSeconds(1),
                HeartbeatInterval = TimeSpan.FromMilliseconds(10)
            });

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(LockStatus.ExecutionFailed, result.Status);
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenHeartbeatFailsDuringShutdown_DoesNotReportSuccess()
    {
        var extensionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockProvider
            .Setup(p => p.AcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProvider
            .Setup(p => p.ExtendLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                string _,
                string _,
                TimeSpan _,
                CancellationToken heartbeatToken) =>
            {
                extensionStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, heartbeatToken);
                }
                catch (OperationCanceledException) when (heartbeatToken.IsCancellationRequested)
                {
                    // Return a definite ownership failure only after callback completion
                    // causes heartbeat shutdown to cancel the in-flight extension.
                }

                return false;
            });
        _mockProvider
            .Setup(p => p.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                CancellationToken.None))
            .ReturnsAsync(true);

        LockResult result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async _ => await extensionStarted.Task,
            "key",
            "value",
            new LockOptions
            {
                CustomExpiry = TimeSpan.FromSeconds(1),
                HeartbeatInterval = TimeSpan.FromMilliseconds(10)
            });

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(LockStatus.ExecutionFailed, result.Status);
    }

    [TestMethod]
    public async Task AcquireAndExecute_ShouldHandleProviderExceptions_Gracefully()
    {
        // Arrange
        _mockProvider.Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection failed"));

        // Act
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) => await Task.Delay(10, ct),
            "key",
            "value");

        // Assert
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(LockStatus.ProviderError, result.Status);
        Assert.IsFalse(result.ErrorMessage?.Contains("Redis connection failed", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AcquireAndExecute_ShouldReleaseLock_WhenCallbackThrows()
    {
        // Arrange
        _mockProvider.Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProvider.Setup(p => p.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            async (ct) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("Business logic failed");
            },
            "key",
            "value");

        // Assert
        Assert.AreEqual(LockStatus.ExecutionFailed, result.Status);

        // Verify Release was called
        _mockProvider.Verify(p => p.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenCallerCancels_PropagatesCancellationAndReleasesLock()
    {
        using var cancellation = new CancellationTokenSource();
        _mockProvider
            .Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), cancellation.Token))
            .ReturnsAsync(true);
        _mockProvider
            .Setup(p => p.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(true);

        Task operation = _distributedLock.AcquireAndExecuteWithLockAsync(
            async callbackToken =>
            {
                await cancellation.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, callbackToken);
            },
            "key",
            "value",
            LockOptions.WithoutHeartbeat,
            cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        _mockProvider.Verify(
            p => p.ReleaseLockAsync(
                "test-instance:key",
                It.Is<string>(owner => owner.StartsWith("value-", StringComparison.Ordinal)),
                CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenCustomExpiryIsInvalid_ThrowsBeforeCallingProvider()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _distributedLock.AcquireAndExecuteWithLockAsync(
                _ => Task.CompletedTask,
                "key",
                "value",
                new LockOptions { CustomExpiry = TimeSpan.Zero }));

        _mockProvider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenHeartbeatIsNotShorterThanExpiry_ThrowsBeforeCallingProvider()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _distributedLock.AcquireAndExecuteWithLockAsync(
                _ => Task.CompletedTask,
                "key",
                "value",
                new LockOptions
                {
                    CustomExpiry = TimeSpan.FromSeconds(1),
                    HeartbeatInterval = TimeSpan.FromSeconds(1)
                }));

        _mockProvider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenReleaseCannotBeConfirmed_DoesNotReportSuccess()
    {
        _mockProvider
            .Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProvider
            .Setup(p => p.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(false);

        LockResult result = await _distributedLock.AcquireAndExecuteWithLockAsync(
            _ => Task.CompletedTask,
            "key",
            "value",
            LockOptions.WithoutHeartbeat);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(LockStatus.ProviderError, result.Status);
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenCancellationCallbackThrows_StillReleasesLock()
    {
        _mockLogger.Setup(logger => logger.IsEnabled(LogLevel.Warning)).Returns(true);
        _mockProvider
            .Setup(p => p.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProvider
            .Setup(p => p.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(true);

        CancellationTokenRegistration registration = default;
        try
        {
            LockResult result = await _distributedLock.AcquireAndExecuteWithLockAsync(
                callbackToken =>
                {
                    registration = callbackToken.Register(() =>
                        throw new InvalidOperationException("application cancellation callback failed"));
                    return Task.CompletedTask;
                },
                "key",
                "value",
                new LockOptions
                {
                    CustomExpiry = TimeSpan.FromSeconds(1),
                    HeartbeatInterval = TimeSpan.FromMilliseconds(100)
                });

            Assert.IsTrue(result.IsSuccess);
        }
        finally
        {
            await registration.DisposeAsync();
        }

        _mockProvider.Verify(
            p => p.ReleaseLockAsync(
                "test-instance:key",
                It.Is<string>(owner => owner.StartsWith("value-", StringComparison.Ordinal)),
                CancellationToken.None),
            Times.Once);
#pragma warning disable CA1873 // Moq expression matchers are not evaluated as production log arguments.
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Warning,
                It.Is<EventId>(eventId => eventId.Id == 12 && eventId.Name == "LogHeartbeatCancellationError"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenOwnerIsOmitted_GeneratesUniqueOwnerPerAcquisition()
    {
        var owners = new List<string>();
        _mockProvider
            .Setup(p => p.AcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, TimeSpan, CancellationToken>((_, owner, _, _) => owners.Add(owner))
            .ReturnsAsync(true);
        _mockProvider
            .Setup(p => p.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                CancellationToken.None))
            .ReturnsAsync(true);

        await _distributedLock.AcquireAndExecuteWithLockAsync(
            _ => Task.CompletedTask,
            "key",
            options: LockOptions.WithoutHeartbeat);
        await _distributedLock.AcquireAndExecuteWithLockAsync(
            _ => Task.CompletedTask,
            "key",
            options: LockOptions.WithoutHeartbeat);

        Assert.HasCount(2, owners);
        Assert.AreNotEqual(owners[0], owners[1]);
        Assert.IsTrue(owners.TrueForAll(owner => owner.Length <= 256));
    }

    [TestMethod]
    public async Task AcquireAndExecute_WhenOwnerPrefixIsRepeated_GeneratesUniqueOwnerPerAcquisition()
    {
        var owners = new List<string>();
        _mockProvider
            .Setup(p => p.AcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, TimeSpan, CancellationToken>((_, owner, _, _) => owners.Add(owner))
            .ReturnsAsync(true);
        _mockProvider
            .Setup(p => p.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                CancellationToken.None))
            .ReturnsAsync(true);

        await _distributedLock.AcquireAndExecuteWithLockAsync(
            _ => Task.CompletedTask,
            "key",
            "worker-a",
            LockOptions.WithoutHeartbeat);
        await _distributedLock.AcquireAndExecuteWithLockAsync(
            _ => Task.CompletedTask,
            "key",
            "worker-a",
            LockOptions.WithoutHeartbeat);

        Assert.HasCount(2, owners);
        Assert.AreNotEqual(owners[0], owners[1]);
        Assert.IsTrue(owners.TrueForAll(owner => owner.StartsWith("worker-a-", StringComparison.Ordinal)));
        Assert.IsTrue(owners.TrueForAll(owner => owner.Length <= 256));
    }

    [TestMethod]
    public async Task CancellationAwareOverload_WithLegacyImplementation_CancelsLinkedCallback()
    {
        IDistributedLock legacyImplementation = new LegacyDistributedLock();
        using var cancellation = new CancellationTokenSource();

        Task operation = legacyImplementation.AcquireAndExecuteWithLockAsync(
            async callbackToken =>
            {
                await cancellation.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, callbackToken);
            },
            "key",
            lockValue: null,
            options: LockOptions.WithoutHeartbeat,
            cancellationToken: cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
    }

    [TestMethod]
    public async Task CancellationAwareOverload_WhenLegacyImplementationTranslatesCancellation_StillPropagatesCallerCancellation()
    {
        IDistributedLock legacyImplementation = new CancellationTranslatingLegacyDistributedLock();
        using var cancellation = new CancellationTokenSource();

        Task operation = legacyImplementation.AcquireAndExecuteWithLockAsync(
            async callbackToken =>
            {
                await cancellation.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, callbackToken);
            },
            "key",
            lockValue: null,
            options: LockOptions.WithoutHeartbeat,
            cancellationToken: cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
    }

    private sealed class LegacyDistributedLock : IDistributedLock
    {
        public async Task<LockResult> AcquireAndExecuteWithLockAsync(
            Func<CancellationToken, Task> callback,
            string lockKey,
            string? lockValue = null,
            LockOptions? options = null)
        {
            await callback(CancellationToken.None);
            return LockResult.Succeeded();
        }
    }

    private sealed class CancellationTranslatingLegacyDistributedLock : IDistributedLock
    {
        public async Task<LockResult> AcquireAndExecuteWithLockAsync(
            Func<CancellationToken, Task> callback,
            string lockKey,
            string? lockValue = null,
            LockOptions? options = null)
        {
            try
            {
                await callback(CancellationToken.None);
                return LockResult.Succeeded();
            }
            catch (OperationCanceledException)
            {
                return LockResult.Failed(LockStatus.ExecutionFailed, "cancelled");
            }
        }
    }
}
