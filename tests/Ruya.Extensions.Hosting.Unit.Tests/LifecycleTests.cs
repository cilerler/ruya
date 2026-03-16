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
using Ruya.Testing.Primitives;

namespace Ruya.Extensions.Hosting.Unit.Tests;

[TestClass]
public class LifecycleTests : TestBase<TestWorkerService>
{
    private Mock<ILogger<TestWorkerService>> _loggerMock = null!;
    private Mock<IDistributedTracing> _tracerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;
    private TestWorkerSettings _settings = null!;
    private TestWorkerService _service = null!;
    private List<IHealthCheck> _healthChecks = null!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<TestWorkerService>>();
        _tracerMock = new Mock<IDistributedTracing>();
        _meterFactoryMock = new Mock<IMeterFactory>();
        _settings = new TestWorkerSettings();
        SetEnabled(_settings, true);
        _healthChecks = new List<IHealthCheck>();

        var meter = new Meter("TestMeter");
        _meterFactoryMock.Setup(m => m.Create(It.IsAny<MeterOptions>())).Returns(meter);

        _service = CreateService();
    }

    private void SetEnabled(WorkerBackgroundServiceSettings settings, bool enabled)
    {
        typeof(WorkerBackgroundServiceSettings)
            .GetProperty(nameof(WorkerBackgroundServiceSettings.Enabled))!
            .SetValue(settings, enabled);
    }

    private TestWorkerService CreateService(IEnumerable<IHealthCheck>? healthChecks = null)
    {
        return new TestWorkerService(
            _loggerMock.Object,
            _tracerMock.Object,
            _meterFactoryMock.Object,
            Options.Create(_settings),
            healthChecks ?? _healthChecks);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _service?.Dispose();
    }

    [TestMethod]
    public async Task StartingAsync_ShouldSucceed_WhenNoHealthChecks()
    {
        // Act
        await _service.StartingAsync(default);

        // Assert - no exception
    }

    [TestMethod]
    public async Task StartingAsync_ShouldSucceed_WhenHealthChecksPass()
    {
        // Arrange
        var healthCheckMock = new Mock<IHealthCheck>();
        healthCheckMock
            .Setup(c => c.CheckHealthAsync(It.IsAny<HealthCheckContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HealthCheckResult.Healthy());
        _healthChecks.Add(healthCheckMock.Object);
        _service = CreateService();

        // Act
        await _service.StartingAsync(default);
    }

    [TestMethod]
    public async Task StartingAsync_ShouldThrow_WhenHealthCheckFails()
    {
        // Arrange
        var healthCheckMock = new Mock<IHealthCheck>();
        healthCheckMock
            .Setup(c => c.CheckHealthAsync(It.IsAny<HealthCheckContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HealthCheckResult.Unhealthy("Database down"));
        _healthChecks.Add(healthCheckMock.Object);
        _service = CreateService();

        // Act & Assert
        try
        {
            await _service.StartingAsync(default);
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Success
        }
    }

    [TestMethod]
    public async Task StartedAsync_ShouldNotStartTask_WhenDisabled()
    {
        // Arrange
        SetEnabled(_settings, false);
        // Re-create service to pick up new settings
        _service = CreateService();

        // Act
        await _service.StartedAsync(default);

        // Assert
        // This is tricky to verify since _executingTask is private. 
        // We can infer by waiting a bit and checking if ExecuteWorkAsync ran (by checking ExecutionCount on our test service).
        // For disabled service, loop shouldn't start.
        await Task.Delay(100);
        Assert.AreEqual(0, _service.ExecutionCount);
    }

    [TestMethod]
    public async Task StartedAsync_ShouldStartTask_WhenEnabled()
    {
        // Arrange
        SetEnabled(_settings, true);
        _settings.ScheduleCronExpression = null; // Implies RunContinuously = true
        _service = CreateService();

        _service.DoWorkAction = async (ct) =>
        {
            // Just complete immediately
            await Task.Yield();
        };

        // Act
        await _service.StartedAsync(default);

        // Assert
        // Give the background task time to spin up and execute at least once
        await Task.Delay(100);
        Assert.IsTrue(_service.ExecutionCount > 0, "Service should have executed at least once");
    }

    [TestMethod]
    public async Task StoppingAsync_ShouldCompleteGracefully_WhenTaskIsRunning()
    {
        // Arrange
        SetEnabled(_settings, true);
        _settings.RunImmediately = true;
        _settings.ShutdownTimeout = TimeSpan.FromSeconds(5);
        _service = CreateService();

        var tcs = new TaskCompletionSource();
        _service.DoWorkAction = async (ct) =>
        {
            try
            {
                // Simulate work that waits for cancellation
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                tcs.SetResult(); // Signal we caught cancellation
                throw;
            }
        };

        await _service.StartedAsync(default);
        // Wait for it to start
        await Task.Delay(50);

        // Act
        // This calls Cancel on CTS, which should cause DoWorkAction to throw OCE, loop catches it, task ends.
        var stopTask = _service.StoppingAsync(default);

        await Task.WhenAny(stopTask, Task.Delay(2000));

        // Assert
        Assert.IsTrue(stopTask.IsCompletedSuccessfully, "StoppingAsync should complete");
        Assert.IsTrue(tcs.Task.IsCompleted, "Work should have been cancelled");
    }

    [TestMethod]
    public async Task StoppingAsync_ShouldReturnReturn_WhenTaskNotStarted()
    {
        // Arrange
        SetEnabled(_settings, false);
        _service = CreateService();
        await _service.StartedAsync(default); // Didn't start loop

        // Act
        await _service.StoppingAsync(default);

        // Assert - no exceptions, completes immediately
    }
}
