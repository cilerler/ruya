using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading;
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
public class HealthStatsTests
{
    private TestWorkerSettings _settings = null!;
    private Mock<IDistributedTracing> _tracerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;
    private Mock<HealthCheckService> _healthCheckServiceMock = null!;
    private Mock<IHostApplicationLifetime> _hostApplicationLifetimeMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _settings = new TestWorkerSettings
        {
            Enabled = true,
            ScheduleCronExpression = null
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
    }

    private TestWorkerService CreateService()
    {
        return new TestWorkerService(
             new Mock<ILogger<TestWorkerService>>().Object,
            _tracerMock.Object,
            _meterFactoryMock.Object,
            Options.Create(_settings),
            _healthCheckServiceMock.Object,
            _hostApplicationLifetimeMock.Object);
    }

    [TestMethod]
    public void GetAverageExecutionDuration_RecordedDurations_ReturnsAverage()
    {
        // Arrange
        using var service = CreateService();

        // Simulate executions
        // Since we can't easily manipulate the private queue directly without running the loop,
        // we can use reflection to call RecordSuccess(duration).

        var recordMethod = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetMethod("RecordSuccess", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(recordMethod, "RecordSuccess method not found");

        recordMethod!.Invoke(service, new object[] { 100.0 });
        recordMethod!.Invoke(service, new object[] { 200.0 });
        recordMethod!.Invoke(service, new object[] { 300.0 });

        // Act
        // Access GetAverageExecutionDuration (public virtual)
        // Wait, checking Service.cs, if it is public I can call it directly.
        // If it is NOT public, I must use reflection.
        // Assuming public virtual based on standard patterns or verifying with reflection first.
        
        var avgMethod = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetMethod(nameof(TestWorkerService.GetAverageExecutionDuration), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        
        var avg = (double)avgMethod!.Invoke(service, null)!;

        // Assert
        Assert.AreEqual(200.0, avg, 0.001, "Average should be 200");
    }

    [TestMethod]
    public void RecordSuccess_SamplesExceedConfiguredSize_MaintainsRollingWindow()
    {
        // Arrange
        _settings.HealthSampleSize = 5;
        using var service = CreateService();

        var recordMethod = typeof(WorkerBackgroundService<TestWorkerSettings>)
             .GetMethod("RecordSuccess", BindingFlags.NonPublic | BindingFlags.Instance);
        
        var avgMethod = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetMethod(nameof(TestWorkerService.GetAverageExecutionDuration), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        // Add 5 items: 10, 10, 10, 10, 10 -> Avg 10
        for (int i = 0; i < 5; i++) recordMethod!.Invoke(service, new object[] { 10.0 });
        Assert.AreEqual(10.0, (double)avgMethod!.Invoke(service, null)!, 0.001);

        // Add 1 more: 20. Window shifts. [10, 10, 10, 10, 20] -> Avg 12
        recordMethod!.Invoke(service, new object[] { 20.0 });
        
        // Assert
        Assert.AreEqual(12.0, (double)avgMethod.Invoke(service, null)!, 0.001);
    }
}
