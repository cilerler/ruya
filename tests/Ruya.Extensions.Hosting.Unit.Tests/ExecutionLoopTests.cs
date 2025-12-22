using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
public class ExecutionLoopTests
{
    private TestWorkerSettings _settings = null!;
    private Mock<IDistributedTracing> _tracerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _settings = new TestWorkerSettings();
        SetEnabled(_settings, true);
        _tracerMock = new Mock<IDistributedTracing>();
        _meterFactoryMock = new Mock<IMeterFactory>();
        _meterFactoryMock.Setup(m => m.Create(It.IsAny<MeterOptions>())).Returns(new Meter("TestMeter"));
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
             new Mock<ILogger<TestWorkerService>>().Object,
            _tracerMock.Object,
            _meterFactoryMock.Object,
            Options.Create(_settings),
            new List<IHealthCheck>());
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_RunImmediately_ShouldExecuteBeforeWait()
    {
        // Arrange
        _settings.RunImmediately = true;
        _settings.ScheduleCronExpression = "0 0 1 1 *"; // Far future cron
        using var service = CreateService();

        var executionSignal = new TaskCompletionSource();
        service.DoWorkAction = (ct) =>
        {
            executionSignal.TrySetResult();
            return Task.CompletedTask;
        };

        // Act
        // Initializing logic
        await service.StartedAsync(default);

        // Assert
        // Should execute once immediately
        var executed = await Task.WhenAny(executionSignal.Task, Task.Delay(2000));
        Assert.AreEqual(executionSignal.Task, executed, "Should have executed immediately");
        
        // Stop service to clean up
        await service.StopAsync(default);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_NoRunImmediately_ShouldWaitFirst()
    {
        // Arrange
        _settings.RunImmediately = false;
        _settings.ScheduleCronExpression = "0 0 1 1 *"; // Far future
        using var service = CreateService();

        service.DoWorkAction = (ct) =>
        {
             // Should not be called immediately
            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        await Task.Delay(200);

        // Assert
        Assert.AreEqual(0, service.ExecutionCount, "Should NOT have executed yet");
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_RunContinuously_ShouldLoopRepeatedly()
    {
        // Arrange
        // RunContinuously is derived from Cron is null/empty
        _settings.ScheduleCronExpression = null; 
        
        using var service = CreateService();
        int executions = 0;
        var tcs = new TaskCompletionSource();

        service.DoWorkAction = (ct) =>
        {
            executions++;
            if (executions >= 3) tcs.TrySetResult();
            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        await Task.WhenAny(tcs.Task, Task.Delay(2000));

        // Assert
        Assert.IsTrue(executions >= 3, "Should have executed multiple times continuously");
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_BreaksLoop_WhenNextOccurrenceIsInfinite()
    {
        // Arrange
        _settings.RunOnce = true; 
        // RunOnce logic in NextOccurrence returns InfiniteTimeSpan.
        // But also check loop logic: if loop calls NextOccurrence and gets Infinite, it breaks.
        // RunImmediately=true ensures ONE execution, then likely breaks.

        _settings.RunImmediately = true;
        using var service = CreateService();

        var tcs = new TaskCompletionSource();
        service.DoWorkAction = (ct) =>
        {
            tcs.TrySetResult(); // First run
            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        await Task.WhenAny(tcs.Task, Task.Delay(1000));

        // Wait a bit more to see if it loops again
        await Task.Delay(100);

        // Assert
        Assert.AreEqual(1, service.ExecutionCount, "Should have executed exactly once because RunOnce=true");
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_DelayBetweenExecutions_ShouldDelayInContinuousMode()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.DelayBetweenExecutions = TimeSpan.FromMilliseconds(500);

        using var service = CreateService();
        var executionTimes = new List<DateTime>();
        var tcs = new TaskCompletionSource();

        service.DoWorkAction = (ct) =>
        {
            executionTimes.Add(DateTime.UtcNow);
            if (executionTimes.Count >= 3) tcs.TrySetResult();
            return Task.CompletedTask;
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        await service.StartedAsync(default);
        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        stopwatch.Stop();

        // Assert
        Assert.IsTrue(executionTimes.Count >= 3, "Should have executed at least 3 times");

        // Verify delays between executions (should be at least 500ms apart)
        for (int i = 1; i < executionTimes.Count; i++)
        {
            var gap = executionTimes[i] - executionTimes[i - 1];
            Assert.IsTrue(gap >= TimeSpan.FromMilliseconds(400),
                $"Gap between execution {i - 1} and {i} was {gap.TotalMilliseconds}ms, expected at least 400ms");
        }

        await service.StopAsync(default);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_DelayBetweenExecutions_ShouldNotDelayWhenZero()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.DelayBetweenExecutions = TimeSpan.Zero; // No delay

        using var service = CreateService();
        var tcs = new TaskCompletionSource();

        service.DoWorkAction = (ct) =>
        {
            if (service.ExecutionCount >= 5) tcs.TrySetResult();
            return Task.CompletedTask;
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        await service.StartedAsync(default);
        await Task.WhenAny(tcs.Task, Task.Delay(2000));
        stopwatch.Stop();

        // Assert
        Assert.IsTrue(service.ExecutionCount >= 5, "Should have executed at least 5 times quickly");
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000,
            $"Should complete quickly without delays, took {stopwatch.ElapsedMilliseconds}ms");

        await service.StopAsync(default);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_DelayBetweenExecutions_ShouldRespectCancellation()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.DelayBetweenExecutions = TimeSpan.FromSeconds(10); // Long delay

        using var service = CreateService();
        var firstExecutionDone = new TaskCompletionSource();

        service.DoWorkAction = (ct) =>
        {
            firstExecutionDone.TrySetResult();
            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        await firstExecutionDone.Task; // Wait for first execution

        var stopwatch = Stopwatch.StartNew();
        await service.StoppingAsync(default); // Request stop during delay
        stopwatch.Stop();

        // Assert - Should stop quickly, not wait for the full 10 second delay
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000,
            $"Should cancel delay quickly, took {stopwatch.ElapsedMilliseconds}ms");
    }
}
