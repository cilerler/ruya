using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.HealthChecks;

namespace Ruya.Services.DistributedLock.Tests;

[TestClass]
public sealed class DistributedLockHealthCheckTests
{
    [TestMethod]
    public async Task CheckHealthAsync_WhenCallerCancelsAfterAcquire_ReleasesProbeAndPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new Mock<IDistributedLockProvider>();
        provider
            .Setup(p => p.AcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                cancellation.Token))
            .ReturnsAsync(true);
        provider
            .Setup(p => p.ExtendLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                cancellation.Token))
            .Returns<string, string, TimeSpan, CancellationToken>(async (_, _, _, token) =>
            {
                await cancellation.CancelAsync();
                return await Task.FromCanceled<bool>(token);
            });
        provider
            .Setup(p => p.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                CancellationToken.None))
            .ReturnsAsync(true);

        var healthCheck = new DistributedLockHealthCheck(provider.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellation.Token));
        provider.Verify(
            p => p.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task CheckHealthAsync_WhenCleanupFails_EmitsStableCleanupEventId()
    {
        var provider = new Mock<IDistributedLockProvider>();
        provider
            .Setup(p => p.AcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        provider
            .Setup(p => p.ExtendLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        provider
            .Setup(p => p.ReleaseLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));
        var logger = new Mock<ILogger<DistributedLockHealthCheck>>();
        logger.Setup(candidate => candidate.IsEnabled(LogLevel.Warning)).Returns(true);
        var healthCheck = new DistributedLockHealthCheck(provider.Object, logger.Object);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Degraded, result.Status);
#pragma warning disable CA1873 // Moq expression matchers are not evaluated as production log arguments.
        logger.Verify(
            candidate => candidate.Log(
                LogLevel.Warning,
                It.Is<EventId>(eventId => eventId.Id == 17 && eventId.Name == "LogHealthCheckCleanupError"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }
}
