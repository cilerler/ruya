using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
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
    private Mock<HealthCheckService> _healthCheckServiceMock = null!;
    private Mock<IHostApplicationLifetime> _hostApplicationLifetimeMock = null!;
    private CapturingLogger _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _settings = new TestWorkerSettings
        {
            Enabled = true
        };
        _tracerMock = new Mock<IDistributedTracing>();
        _meterFactoryMock = new Mock<IMeterFactory>();
        _meterFactoryMock.Setup(m => m.Create(It.IsAny<MeterOptions>())).Returns(TestMeters.Create);
        _healthCheckServiceMock = new Mock<HealthCheckService>();
        _healthCheckServiceMock
            .Setup(service => service.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                TimeSpan.Zero));
        _hostApplicationLifetimeMock = new Mock<IHostApplicationLifetime>();
        _logger = new CapturingLogger();
    }

    private TestWorkerService CreateService()
    {
        return new TestWorkerService(
            _logger,
            _tracerMock.Object,
            _meterFactoryMock.Object,
            Options.Create(_settings),
            _healthCheckServiceMock.Object,
            _hostApplicationLifetimeMock.Object);
    }

    private Task<(string Message, int ExecutionCount)> CaptureLogAsync(
        int eventId,
        Func<int> getExecutionCount)
    {
        var logObserved = new TaskCompletionSource<(string Message, int ExecutionCount)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _logger.Observe(
            eventId,
            message => logObserved.TrySetResult((message, getExecutionCount())));

        return logObserved.Task;
    }

    private sealed class CapturingLogger : ILogger<TestWorkerService>
    {
        private readonly object _sync = new();
        private int _observedEventId = -1;
        private Action<string>? _observer;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Action<string>? observer;
            lock (_sync)
            {
                observer = eventId.Id == _observedEventId ? _observer : null;
            }

            observer?.Invoke(formatter(state, exception));
        }

        public void Observe(int eventId, Action<string> observer)
        {
            lock (_sync)
            {
                _observedEventId = eventId;
                _observer = observer;
            }
        }
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_RunImmediatelyIsTrue_ExecutesBeforeScheduleWait()
    {
        // Arrange
        _settings.RunImmediately = true;
        _settings.ScheduleCronExpression = "0 0 * * * *";
        await using var service = CreateService();

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
        await executionSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, service.ExecutionCount);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_RunImmediatelyIsFalse_WaitsForFirstSchedule()
    {
        // Arrange
        _settings.RunImmediately = false;
        _settings.ScheduleCronExpression = "0 0 * * * *";
        await using var service = CreateService();
        var scheduleWaitObserved = CaptureLogAsync(1016, () => service.ExecutionCount);

        service.DoWorkAction = (ct) =>
        {
             // Should not be called immediately
            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        var observation = await scheduleWaitObserved.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.AreEqual(0, observation.ExecutionCount, "The first cron wait must begin before any work executes.");
        StringAssert.Contains(observation.Message, "Next execution in", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_ContinuousMode_RepeatsExecutions()
    {
        // Arrange
        // RunContinuously is derived from Cron is null/empty
        _settings.ScheduleCronExpression = null; 
        
        await using var service = CreateService();
        var executionSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        service.DoWorkAction = (ct) =>
        {
            if (service.ExecutionCount >= 3)
            {
                executionSignal.TrySetResult();
            }

            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        await executionSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.IsTrue(service.ExecutionCount >= 3, "Should have executed multiple times continuously");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RunScheduleLoopAsync_RunOnceWithEitherImmediateValue_ExecutesExactlyOnce(bool runImmediately)
    {
        // Arrange
        _settings.RunOnce = true;
        _settings.RunImmediately = runImmediately;
        await using var service = CreateService();
        var runOnceCompleted = CaptureLogAsync(1013, () => service.ExecutionCount);

        // Act
        await service.StartedAsync(default);
        var observation = await runOnceCompleted.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.AreEqual(1, observation.ExecutionCount, "RunOnce should execute exactly once before completing.");
        Assert.AreEqual(1, service.ExecutionCount, "RunOnce should leave no work scheduled.");
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_ExecutionIsStillRunning_StartsNextExecutionSequentially()
    {
        // Arrange
        _settings.ScheduleCronExpression = null;
        await using var service = CreateService();

        var firstExecutionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondExecutionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        service.DoWorkAction = async (ct) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstExecutionStarted.TrySetResult();
                await releaseFirstExecution.Task;
            }
            else if (call == 2)
            {
                secondExecutionStarted.TrySetResult();
            }
        };

        try
        {
            // Act
            await service.StartedAsync(default);
            await firstExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(1, service.ExecutionCount, "A second execution must not start while the first is held.");

            releaseFirstExecution.TrySetResult();
            await secondExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            releaseFirstExecution.TrySetResult();
        }
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_ContinuousDelayIsConfigured_AppliesDelayBetweenExecutions()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.DelayBetweenExecutions = TimeSpan.FromDays(1);

        await using var service = CreateService();
        var delayObserved = CaptureLogAsync(1014, () => service.ExecutionCount);

        // Act
        await service.StartedAsync(default);
        var observation = await delayObserved.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.AreEqual(1, observation.ExecutionCount, "The configured delay must begin after the first execution.");
        StringAssert.Contains(observation.Message, "1.00:00:00", StringComparison.Ordinal);
        StringAssert.Contains(observation.Message, "Idle cycle: False", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_ContinuousDelayIsZero_DoesNotDelay()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.DelayBetweenExecutions = TimeSpan.Zero; // No delay

        await using var service = CreateService();
        var executionSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        service.DoWorkAction = (ct) =>
        {
            if (service.ExecutionCount >= 5)
            {
                executionSignal.TrySetResult();
            }

            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        await executionSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.IsTrue(service.ExecutionCount >= 5, "A zero delay should allow the continuous loop to advance.");
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_IdleCycleHasBackoffConfigured_AppliesIdleBackoff()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.IdleBackoffDuration = TimeSpan.FromHours(12);

        await using var service = CreateService();
        var delayObserved = CaptureLogAsync(1014, () => service.ExecutionCount);

        service.DoWorkAction = (ct) =>
        {
            service.SetIdleCycle(true); // Simulate no data found
            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        var observation = await delayObserved.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.AreEqual(1, observation.ExecutionCount, "Idle backoff must begin after the idle execution.");
        StringAssert.Contains(observation.Message, "12:00:00", StringComparison.Ordinal);
        StringAssert.Contains(observation.Message, "Idle cycle: True", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_IdleCycleHasBothDelaysConfigured_AppliesOnlyIdleBackoff()
    {
        _settings.ScheduleCronExpression = null;
        _settings.IdleBackoffDuration = TimeSpan.FromHours(12);
        _settings.DelayBetweenExecutions = TimeSpan.FromDays(1);

        await using var service = CreateService();
        var delayObserved = CaptureLogAsync(1014, () => service.ExecutionCount);
        service.DoWorkAction = cancellationToken =>
        {
            service.SetIdleCycle(true);
            return Task.CompletedTask;
        };

        await service.StartedAsync(default);

        var observation = await delayObserved.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, observation.ExecutionCount);
        StringAssert.Contains(observation.Message, "12:00:00", StringComparison.Ordinal);
        StringAssert.Contains(observation.Message, "Idle cycle: True", StringComparison.Ordinal);
        Assert.IsFalse(observation.Message.Contains("1.00:00:00", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_NonIdleCycleHasBackoffConfigured_DoesNotApplyIdleBackoff()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.IdleBackoffDuration = TimeSpan.FromSeconds(10); // Large backoff, but should not apply

        await using var service = CreateService();
        var executionSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        service.DoWorkAction = (ct) =>
        {
            // IdleCycle defaults to false (reset by base class), so no backoff
            if (service.ExecutionCount >= 5)
            {
                executionSignal.TrySetResult();
            }

            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        await executionSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.IsTrue(service.ExecutionCount >= 5, "Non-idle work should not use the configured idle backoff.");
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_IdleBackoffIsZero_DoesNotDelay()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.IdleBackoffDuration = TimeSpan.Zero; // Disabled

        await using var service = CreateService();
        var executionSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        service.DoWorkAction = (ct) =>
        {
            service.SetIdleCycle(true); // Idle, but backoff is disabled
            if (service.ExecutionCount >= 5)
            {
                executionSignal.TrySetResult();
            }

            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        await executionSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.IsTrue(service.ExecutionCount >= 5, "A zero idle backoff should allow the loop to advance.");
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_CancellationDuringIdleBackoff_StopsPromptly()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.IdleBackoffDuration = TimeSpan.FromHours(12);

        await using var service = CreateService();
        var delayObserved = CaptureLogAsync(1014, () => service.ExecutionCount);

        service.DoWorkAction = (ct) =>
        {
            service.SetIdleCycle(true); // Trigger idle backoff
            return Task.CompletedTask;
        };

        // Act
        await service.StartedAsync(default);
        var observation = await delayObserved.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StoppingAsync(default).WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.AreEqual(1, observation.ExecutionCount);
        StringAssert.Contains(observation.Message, "Idle cycle: True", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_AlternatingIdleCycles_ResetsIdleStateBetweenExecutions()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode

        await using var service = CreateService();
        var idleStateAtSecondExecution = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.DoWorkAction = async (ct) =>
        {
            if (service.ExecutionCount == 1)
            {
                service.SetIdleCycle(true);
                return;
            }

            idleStateAtSecondExecution.TrySetResult(service.CurrentIdleCycle);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };

        // Act
        await service.StartedAsync(default);
        var observedIdleState = await idleStateAtSecondExecution.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StoppingAsync(default).WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.IsFalse(observedIdleState, "The base loop must reset idle state before each execution.");
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_CancellationDuringContinuousDelay_StopsPromptly()
    {
        // Arrange
        _settings.ScheduleCronExpression = null; // Continuous mode
        _settings.DelayBetweenExecutions = TimeSpan.FromDays(1);

        await using var service = CreateService();
        var delayObserved = CaptureLogAsync(1014, () => service.ExecutionCount);

        // Act
        await service.StartedAsync(default);
        var observation = await delayObserved.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StoppingAsync(default).WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.AreEqual(1, observation.ExecutionCount);
        StringAssert.Contains(observation.Message, "1.00:00:00", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RunScheduleLoopAsync_CronModeHasContinuousDelayConfigured_WaitsOnlyForCronOccurrence()
    {
        _settings.RunImmediately = false;
        _settings.ScheduleCronExpression = "0 0 * * * *";
        _settings.DelayBetweenExecutions = TimeSpan.FromDays(1);

        await using var service = CreateService();
        var scheduleWaitObserved = CaptureLogAsync(1016, () => service.ExecutionCount);

        await service.StartedAsync(default);

        var observation = await scheduleWaitObserved.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(0, observation.ExecutionCount, "Cron mode must not run work before its first occurrence.");
        StringAssert.Contains(observation.Message, "Next execution in", StringComparison.Ordinal);
    }
}
