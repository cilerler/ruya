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
public class HealthStatsTests
{
    private TestWorkerSettings _settings = null!;
    private Mock<IDistributedTracing> _tracerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _settings = new TestWorkerSettings();
        SetEnabled(_settings, true);
        _settings.ScheduleCronExpression = null; // Continuous
        
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
    public async Task GetAverageExecutionDuration_ShouldReturnCorrectAverage()
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
            .GetMethod("GetAverageExecutionDuration", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        
        var avg = (double)avgMethod!.Invoke(service, null)!;

        // Assert
        Assert.AreEqual(200.0, avg, 0.001, "Average should be 200");
    }

    [TestMethod]
    public async Task RecordSuccess_ShouldMaintainRollingWindow()
    {
        // Arrange
        _settings.HealthSampleSize = 5;
        using var service = CreateService();

        var recordMethod = typeof(WorkerBackgroundService<TestWorkerSettings>)
             .GetMethod("RecordSuccess", BindingFlags.NonPublic | BindingFlags.Instance);
        
        var avgMethod = typeof(WorkerBackgroundService<TestWorkerSettings>)
            .GetMethod("GetAverageExecutionDuration", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

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
