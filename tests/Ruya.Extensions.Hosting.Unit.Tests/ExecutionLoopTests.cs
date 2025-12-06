using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
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
}
