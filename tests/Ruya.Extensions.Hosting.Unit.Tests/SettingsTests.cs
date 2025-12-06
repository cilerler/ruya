using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Extensions.Hosting.Unit.Tests;

[TestClass]
public class SettingsTests
{
    private void SetEnabled(WorkerBackgroundServiceSettings settings, bool enabled)
    {
        typeof(WorkerBackgroundServiceSettings)
            .GetProperty(nameof(WorkerBackgroundServiceSettings.Enabled))!
            .SetValue(settings, enabled);
    }

    [TestMethod]
    public void NextOccurrence_ShouldReturnInfinite_WhenDisabled()
    {
        // Arrange
        var settings = new TestWorkerSettings();
        SetEnabled(settings, false);

        // Act
        var next = settings.NextOccurrence;

        // Assert
        Assert.AreEqual(System.Threading.Timeout.InfiniteTimeSpan, next);
    }

    [TestMethod]
    public void NextOccurrence_ShouldReturnInfinite_WhenRunOnce()
    {
        // Arrange
        var settings = new TestWorkerSettings();
        SetEnabled(settings, true);
        settings.RunOnce = true;

        // Act
        var next = settings.NextOccurrence;

        // Assert
        Assert.AreEqual(System.Threading.Timeout.InfiniteTimeSpan, next);
    }

    [TestMethod]
    public void NextOccurrence_ShouldReturnZero_WhenRunContinuously()
    {
        // Arrange
        var settings = new TestWorkerSettings();
        SetEnabled(settings, true);
        settings.ScheduleCronExpression = null; // Implies RunContinuously

        // Act
        var next = settings.NextOccurrence;

        // Assert
        Assert.AreEqual(TimeSpan.Zero, next);
    }

    [TestMethod]
    public void NextOccurrence_ShouldReturnTimeSpan_WhenCronIsValid()
    {
        // Arrange
        var settings = new TestWorkerSettings();
        SetEnabled(settings, true);
        settings.ScheduleCronExpression = "* * * * *"; // Every minute

        // Act
        var next = settings.NextOccurrence;

        // Assert
        Assert.IsTrue(next > TimeSpan.Zero, "Should be in the future");
        Assert.IsTrue(next < TimeSpan.FromMinutes(2), "Should be within a minute (and a bit)");
    }

    [TestMethod]
    public void NextOccurrence_ShouldThrow_WhenCronIsInvalid()
    {
        // Arrange
        var settings = new TestWorkerSettings();
        SetEnabled(settings, true);
        settings.ScheduleCronExpression = "invalid cron";

        // Act & Assert
        try
        {
            var _ = settings.NextOccurrence;
            Assert.Fail("Should have thrown CronFormatException");
        }
        catch (Cronos.CronFormatException)
        {
            // Success
        }
    }
}
