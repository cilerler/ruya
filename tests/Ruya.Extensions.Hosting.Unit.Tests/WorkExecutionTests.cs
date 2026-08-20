using System;
using System.Collections.Generic;
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
public class WorkExecutionTests
{
    private TestWorkerSettings _settings = null!;
    private Mock<IDistributedTracing> _tracerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;
    private Mock<ILogger<TestWorkerService>> _loggerMock = null!;
    private Mock<HealthCheckService> _healthCheckServiceMock = null!;
    private Mock<IHostApplicationLifetime> _hostApplicationLifetimeMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _settings = new TestWorkerSettings
        {
            Enabled = true
        };
        _tracerMock = new Mock<IDistributedTracing>();
        _meterFactoryMock = new Mock<IMeterFactory>();
        _meterFactoryMock.Setup(factory => factory.Create(It.IsAny<MeterOptions>())).Returns(TestMeters.Create);
        _loggerMock = new Mock<ILogger<TestWorkerService>>();
        _healthCheckServiceMock = new Mock<HealthCheckService>();
        _healthCheckServiceMock
            .Setup(service => service.CheckHealthAsync(
                It.IsAny<Func<HealthCheckRegistration, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                TimeSpan.Zero));
        _hostApplicationLifetimeMock = new Mock<IHostApplicationLifetime>();
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

    private static Task InvokeExecuteWorkAsync(TestWorkerService service, CancellationToken cancellationToken = default)
    {
        var method = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetMethod("ExecuteWorkAsync", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(method);
        return (Task)method.Invoke(service, [cancellationToken])!;
    }

    private static TimeSpan InvokeCalculateBackoffWithJitter(TestWorkerService service, int attempt)
    {
        var method = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetMethod("CalculateBackoffWithJitter", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(method);
        return (TimeSpan)method.Invoke(service, [attempt])!;
    }

    [TestMethod]
    public async Task ExecuteWorkAsync_TransientFailuresWithinRetryLimit_CompletesSuccessfully()
    {
        _settings.RetryEnabled = true;
        _settings.RetryCount = 2;
        _settings.RetryBaseDelaySeconds = 0;
        _settings.RetryMaxDelaySeconds = 1;

        using var service = CreateService();
        service.TransientExceptionPredicate = exception => exception is TimeoutException;

        var attempts = 0;
        service.DoWorkAction = cancellationToken =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new TimeoutException("Transient failure.");
            }

            return Task.CompletedTask;
        };

        await InvokeExecuteWorkAsync(service);

        Assert.AreEqual(3, attempts);
        _hostApplicationLifetimeMock.Verify(lifetime => lifetime.StopApplication(), Times.Never);
        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Warning,
                It.Is<EventId>(eventId => eventId.Id == 1021 && eventId.Name == "ExecutionRetrying"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [TestMethod]
    public async Task ExecuteWorkAsync_NonTransientFailure_StopsApplicationAndRethrowsWithoutRetry()
    {
        _settings.RetryEnabled = true;
        _settings.RetryCount = 3;
        _settings.RetryBaseDelaySeconds = 0;

        using var service = CreateService();
        service.TransientExceptionPredicate = exception => exception is TimeoutException;

        var attempts = 0;
        service.DoWorkAction = cancellationToken =>
        {
            attempts++;
            throw new InvalidOperationException("Fatal failure.");
        };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => InvokeExecuteWorkAsync(service));

        Assert.AreEqual(1, attempts);
        _hostApplicationLifetimeMock.Verify(lifetime => lifetime.StopApplication(), Times.Once);
        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.Is<EventId>(eventId => eventId.Id == 1020 && eventId.Name == "ExecutionFailed"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteWorkAsync_TransientFailureExhaustsRetryLimit_StopsApplicationAndRethrows()
    {
        _settings.RetryEnabled = true;
        _settings.RetryCount = 2;
        _settings.RetryBaseDelaySeconds = 0;
        _settings.RetryMaxDelaySeconds = 1;

        using var service = CreateService();
        service.TransientExceptionPredicate = exception => exception is TimeoutException;

        var attempts = 0;
        service.DoWorkAction = cancellationToken =>
        {
            attempts++;
            throw new TimeoutException("Transient failure.");
        };

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => InvokeExecuteWorkAsync(service));

        Assert.AreEqual(3, attempts);
        _hostApplicationLifetimeMock.Verify(lifetime => lifetime.StopApplication(), Times.Once);
    }

    [TestMethod]
    public void CalculateBackoffWithJitter_ExponentialDelayExceedsConfiguredMaximum_DoesNotExceedMaximum()
    {
        _settings.RetryBaseDelaySeconds = 4;
        _settings.RetryMaxDelaySeconds = 5;

        using var service = CreateService();

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var delay = InvokeCalculateBackoffWithJitter(service, attempt);
            Assert.IsTrue(
                delay <= TimeSpan.FromSeconds(_settings.RetryMaxDelaySeconds),
                $"Attempt {attempt} produced {delay}, which exceeds the configured maximum.");
        }
    }

    [TestMethod]
    public async Task ExecuteWorkAsync_HostCancellation_CancelsWithoutStoppingApplication()
    {
        using var cancellationSource = new CancellationTokenSource();
        using var service = CreateService();
        service.DoWorkAction = cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        await cancellationSource.CancelAsync();
        await InvokeExecuteWorkAsync(service, cancellationSource.Token);

        _hostApplicationLifetimeMock.Verify(lifetime => lifetime.StopApplication(), Times.Never);
    }
}
