using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
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
public class LifecycleTests
{
    private Mock<ILogger<TestWorkerService>> _loggerMock = null!;
    private Mock<IDistributedTracing> _tracerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;
    private Mock<HealthCheckService> _healthCheckServiceMock = null!;
    private Mock<IHostApplicationLifetime> _hostApplicationLifetimeMock = null!;
    private TestWorkerSettings _settings = null!;
    private TestWorkerService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<TestWorkerService>>();
        _tracerMock = new Mock<IDistributedTracing>();
        _meterFactoryMock = new Mock<IMeterFactory>();
        _meterFactoryMock
            .Setup(factory => factory.Create(It.IsAny<MeterOptions>()))
            .Returns(TestMeters.Create);
        _healthCheckServiceMock = new Mock<HealthCheckService>();
        _healthCheckServiceMock
            .Setup(service => service.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateHealthReport());
        _hostApplicationLifetimeMock = new Mock<IHostApplicationLifetime>();
        _settings = new TestWorkerSettings
        {
            Enabled = true
        };
        _service = CreateService();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_service is not null)
        {
            await _service.DisposeAsync();
        }
    }

    private static HealthReport CreateHealthReport(
        string? name = null,
        HealthStatus status = HealthStatus.Healthy)
    {
        var entries = new Dictionary<string, HealthReportEntry>();
        if (name is not null)
        {
            entries.Add(
                name,
                new HealthReportEntry(
                    status,
                    status.ToString(),
                    TimeSpan.Zero,
                    null,
                    new Dictionary<string, object>()));
        }

        return new HealthReport(entries, TimeSpan.Zero);
    }

    private TestWorkerService CreateService()
    {
        return new TestWorkerService(
            _loggerMock.Object,
            _tracerMock.Object,
            _meterFactoryMock.Object,
            Options.Create(_settings),
            _healthCheckServiceMock.Object,
            _hostApplicationLifetimeMock.Object);
    }

    private static Task? GetExecutingTask(TestWorkerService service)
    {
        var field = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetField("_executingTask", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field);
        return (Task?)field.GetValue(service);
    }

    private static void DisposeFaultedService(TestWorkerService service) => service.Dispose();

    [TestMethod]
    public async Task StartingAsync_NoStartupHealthChecks_CompletesSuccessfully()
    {
        await _service.StartingAsync(default);

        _healthCheckServiceMock.Verify(
            service => service.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task StartingAsync_HealthRegistrationsHaveDifferentTags_SelectsOnlyStartupTag()
    {
        Func<HealthCheckRegistration, bool>? capturedPredicate = null;
        _healthCheckServiceMock
            .Setup(service => service.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Func<HealthCheckRegistration, bool>, CancellationToken>((predicate, _) =>
                capturedPredicate = predicate)
            .ReturnsAsync(CreateHealthReport());

        await _service.StartingAsync(default);

        Assert.IsNotNull(capturedPredicate);
        var startupRegistration = new HealthCheckRegistration(
            "database",
            _ => Mock.Of<IHealthCheck>(),
            null,
            ["startup", "ready"]);
        var readinessRegistration = new HealthCheckRegistration(
            "worker",
            _ => throw new AssertFailedException("The worker readiness check must not be resolved during startup."),
            null,
            ["ready"]);

        Assert.IsTrue(capturedPredicate(startupRegistration));
        Assert.IsFalse(capturedPredicate(readinessRegistration));
    }

    [TestMethod]
    public async Task StartingAsync_StartupHealthChecksAreHealthy_CompletesSuccessfully()
    {
        _healthCheckServiceMock
            .Setup(service => service.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateHealthReport("database", HealthStatus.Healthy));

        await _service.StartingAsync(default);
    }

    [TestMethod]
    [DataRow(HealthStatus.Degraded)]
    [DataRow(HealthStatus.Unhealthy)]
    public async Task StartingAsync_StartupHealthCheckIsNotHealthy_Throws(HealthStatus status)
    {
        _healthCheckServiceMock
            .Setup(service => service.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateHealthReport("database", status));

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _service.StartingAsync(default));

        StringAssert.Contains(exception.Message, $"database={status}", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task StartingAsync_WorkerIsDisabled_SkipsStartupHealthChecks()
    {
        _settings.Enabled = false;

        await _service.StartingAsync(default);

        _healthCheckServiceMock.Verify(
            service => service.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task StartedAsync_WorkerIsDisabled_DoesNotStartExecutionLoop()
    {
        _settings.Enabled = false;

        await _service.StartedAsync(default);

        Assert.IsNull(GetExecutingTask(_service));
        Assert.AreEqual(0, _service.ExecutionCount);
    }

    [TestMethod]
    public async Task StartedAsync_WorkerIsEnabled_StartsExecutionLoop()
    {
        _settings.ScheduleCronExpression = null;
        var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DoWorkAction = cancellationToken =>
        {
            executed.TrySetResult();
            return Task.CompletedTask;
        };

        await _service.StartedAsync(default);
        await executed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsNotNull(GetExecutingTask(_service));
        Assert.IsTrue(_service.ExecutionCount > 0);
    }

    [TestMethod]
    public async Task StoppingAsync_StartedWorkerFailsFatally_StopsApplicationAndRethrowsFault()
    {
        _settings.RunOnce = true;
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _hostApplicationLifetimeMock
            .Setup(lifetime => lifetime.StopApplication())
            .Callback(() => stopRequested.TrySetResult());
        var expectedFailure = new InvalidOperationException("Fatal worker failure.");
        _service.DoWorkAction = cancellationToken =>
            throw expectedFailure;

        await _service.StartedAsync(default);
        await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var actualFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _service.StoppingAsync(default));
        Assert.AreSame(expectedFailure, actualFailure);
        _hostApplicationLifetimeMock.Verify(lifetime => lifetime.StopApplication(), Times.Once);

        DisposeFaultedService(_service);
        _service = null!;
    }

    [TestMethod]
    public async Task StoppingAsync_StartedWorkerExhaustsTransientRetries_StopsApplicationAndRethrowsFault()
    {
        _settings.RunOnce = true;
        _settings.RetryEnabled = true;
        _settings.RetryCount = 1;
        _settings.RetryBaseDelaySeconds = 0;
        _settings.RetryMaxDelaySeconds = 1;
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _hostApplicationLifetimeMock
            .Setup(lifetime => lifetime.StopApplication())
            .Callback(() => stopRequested.TrySetResult());
        _service.TransientExceptionPredicate = exception => exception is TimeoutException;
        var attempts = 0;
        TimeoutException? finalFailure = null;
        _service.DoWorkAction = cancellationToken =>
        {
            attempts++;
            finalFailure = new TimeoutException("Transient worker failure.");
            throw finalFailure;
        };

        await _service.StartedAsync(default);
        await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var actualFailure = await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => _service.StoppingAsync(default));
        Assert.AreEqual(2, attempts);
        Assert.AreSame(finalFailure, actualFailure);
        _hostApplicationLifetimeMock.Verify(lifetime => lifetime.StopApplication(), Times.Once);

        DisposeFaultedService(_service);
        _service = null!;
    }

    [TestMethod]
    public async Task StoppingAsync_ExecutionIsWaitingForCancellation_CompletesGracefully()
    {
        _settings.ScheduleCronExpression = null;
        _settings.ShutdownTimeout = TimeSpan.FromSeconds(5);
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DoWorkAction = async cancellationToken =>
        {
            executionStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                executionCancelled.TrySetResult();
                throw;
            }
        };

        await _service.StartedAsync(default);
        await executionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await _service.StoppingAsync(default);

        Assert.IsTrue(executionCancelled.Task.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task StoppingAsync_ExecutionIgnoresCancellation_ReturnsAtConfiguredTimeout()
    {
        _settings.ScheduleCronExpression = null;
        _settings.ShutdownTimeout = TimeSpan.FromMilliseconds(100);
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DoWorkAction = cancellationToken =>
        {
            executionStarted.TrySetResult();
            return releaseExecution.Task;
        };

        try
        {
            await _service.StartedAsync(default);
            await executionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var stopwatch = Stopwatch.StartNew();
            await _service.StoppingAsync(default);
            stopwatch.Stop();

            Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(50));
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(1),
                $"Shutdown should be bounded by the configured timeout; elapsed {stopwatch.Elapsed}.");
        }
        finally
        {
            releaseExecution.TrySetResult();
        }
    }

    [TestMethod]
    public async Task StoppingAsync_HostCancellationIsRequested_StopsWaitingPromptly()
    {
        _settings.ScheduleCronExpression = null;
        _settings.ShutdownTimeout = TimeSpan.FromSeconds(5);
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _service.DoWorkAction = cancellationToken =>
        {
            executionStarted.TrySetResult();
            return releaseExecution.Task;
        };

        try
        {
            await _service.StartedAsync(default);
            await executionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var hostCancellation = new CancellationTokenSource();
            await hostCancellation.CancelAsync();

            var stopwatch = Stopwatch.StartNew();
            await _service.StoppingAsync(hostCancellation.Token);
            stopwatch.Stop();

            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(1),
                $"Host cancellation should stop waiting promptly; elapsed {stopwatch.Elapsed}.");
        }
        finally
        {
            releaseExecution.TrySetResult();
        }
    }

    [TestMethod]
    public async Task StoppingAsync_ExecutionLoopWasNotStarted_ReturnsWithoutWork()
    {
        _settings.Enabled = false;
        await _service.StartedAsync(default);

        await _service.StoppingAsync(default);

        Assert.AreEqual(0, _service.ExecutionCount);
    }
}
