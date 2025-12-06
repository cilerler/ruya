using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Extensions.Hosting;

namespace Ruya.Extensions.Hosting.Unit.Tests;

[TestClass]
public class WorkExecutionTests
{
    private TestWorkerSettings _settings = null!;
    private Mock<IDistributedTracing> _tracerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;
    // Use the logger type expected by the base class logic if we verify logs from base
    // The base class constructor expects ILogger, but likely cast/injects it.
    // However, TestWorkerService constructor takes ILogger<TestWorkerService>.
    // To verify logs from Base class (WorkerBackgroundService), we need to ensure they share the mock or we mock the one used by base.
    // TestWorkerService passes the logger to base. So mocking ILogger<TestWorkerService> is sufficient as it is an ILogger.
    private Mock<ILogger<TestWorkerService>> _loggerMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _settings = new TestWorkerSettings(); 
        SetEnabled(_settings, true);
        
        _tracerMock = new Mock<IDistributedTracing>();
        _meterFactoryMock = new Mock<IMeterFactory>();
        _meterFactoryMock.Setup(m => m.Create(It.IsAny<MeterOptions>())).Returns(new Meter("TestMeter"));
        _loggerMock = new Mock<ILogger<TestWorkerService>>();
    }

    private void SetEnabled(WorkerBackgroundServiceSettings settings, bool enabled)
    {
        typeof(WorkerBackgroundServiceSettings)
            .GetProperty(nameof(WorkerBackgroundServiceSettings.Enabled))!
            .SetValue(settings, enabled);
    }

    private TestWorkerService CreateService()
    {
        return new TestWorkerService(
            _loggerMock.Object,
            _tracerMock.Object,
            _meterFactoryMock.Object,
            Options.Create(_settings),
            new List<IHealthCheck>());
    }

    [TestMethod]
    public async Task ExecuteWorkAsync_Skips_IfPreviousStillRunning()
    {
        // This simulates: The first execution starts and holds the lock.
        // A second execution is attempted concurrently.
        using var service = CreateService();
        var entryBarrier = new TaskCompletionSource();
        var exitBarrier = new TaskCompletionSource();

        service.DoWorkAction = async (ct) =>
        {
            entryBarrier.TrySetResult(); // Signal we are in
            await exitBarrier.Task;   // Wait to finish
        };

        // Invoke via reflection to test internal "ExecuteWorkAsync" directly
        var method = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetMethod("ExecuteWorkAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        
        // Start task 1
        var task1 = (Task)method!.Invoke(service, new object[] { CancellationToken.None })!;

        await entryBarrier.Task; // Wait for task1 to grab lock

        // Try task 2 concurrently
        var task2 = (Task)method.Invoke(service, new object[] { CancellationToken.None })!;
        await task2; // Should return immediately (skipped) and not wait for exitBarrier

        // Cleanup
        exitBarrier.TrySetResult();
        await task1;

        // Verify logger logged "Skipping execution"
         _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Skipping execution")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteWithRetryAsync_RetriesOnFailure()
    {
        // Settings: 3 retries
        _settings.RetryEnabled = true;
        _settings.RetryCount = 2; // +1 initial = 3 attempts total
        _settings.RetryBaseDelaySeconds = 0; // Fast retry

        using var service = CreateService();
        int attempts = 0;
        service.DoWorkAction = (ct) =>
        {
            attempts++;
            if (attempts < 3) throw new InvalidOperationException("Fail");
            return Task.CompletedTask;
        };

        // Invoke via reflection
        var method = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetMethod("ExecuteWorkAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        
        await (Task)method!.Invoke(service, new object[] { CancellationToken.None })!;

        Assert.AreEqual(3, attempts, "Should have attempted 3 times (2 failures, 1 success)");
        
        // Verify logs
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrying in")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [TestMethod]
    public async Task ExecuteWithRetryAsync_FailsAfterMaxRetries()
    {
        _settings.RetryEnabled = true;
        _settings.RetryCount = 1; 

        using var service = CreateService();
        service.DoWorkAction = (ct) => throw new Exception("Persistent Failure");

        var method = typeof(WorkerBackgroundService<TestWorkerSettings>)
             .GetMethod("ExecuteWorkAsync", BindingFlags.NonPublic | BindingFlags.Instance);

        await (Task)method!.Invoke(service, new object[] { CancellationToken.None })!;

        // Should log Error eventually
         _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Execution failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
