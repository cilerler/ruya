using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Extensions.Hosting.Unit.Tests;

[TestClass]
public class SettingsTests
{
    private static ServiceProvider BuildValidatedOptions(Action<TestWorkerSettings>? configure = null)
    {
        var services = new ServiceCollection();
        var options = services
            .AddOptions<TestWorkerSettings>()
            .ValidateDataAnnotations()
            .Validate(
                settings => settings.RetryMaxDelaySeconds >= settings.RetryBaseDelaySeconds,
                "RetryMaxDelaySeconds must be greater than or equal to RetryBaseDelaySeconds.")
            .Validate(
                settings => settings.HealthHardTimeout is null || settings.HealthHardTimeout > TimeSpan.Zero,
                "HealthHardTimeout must be positive when configured.")
            .Validate(
                settings => settings.ShutdownTimeout > TimeSpan.Zero,
                "ShutdownTimeout must be positive.")
            .Validate(
                settings => settings.DelayBetweenExecutions >= TimeSpan.Zero,
                "DelayBetweenExecutions cannot be negative.")
            .Validate(
                settings => settings.IdleBackoffDuration >= TimeSpan.Zero,
                "IdleBackoffDuration cannot be negative.")
            .ValidateOnStart();

        if (configure is not null)
        {
            options.Configure(configure);
        }

        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void NextOccurrence_WorkerIsDisabled_ReturnsInfinite()
    {
        // Arrange
        var settings = new TestWorkerSettings
        {
            Enabled = false
        };

        // Act
        var next = settings.NextOccurrence;

        // Assert
        Assert.AreEqual(System.Threading.Timeout.InfiniteTimeSpan, next);
    }

    [TestMethod]
    public void NextOccurrence_RunOnceIsEnabled_ReturnsInfinite()
    {
        // Arrange
        var settings = new TestWorkerSettings
        {
            Enabled = true,
            RunOnce = true
        };

        // Act
        var next = settings.NextOccurrence;

        // Assert
        Assert.AreEqual(System.Threading.Timeout.InfiniteTimeSpan, next);
    }

    [TestMethod]
    public void NextOccurrence_WorkerRunsContinuously_ReturnsZero()
    {
        // Arrange
        var settings = new TestWorkerSettings
        {
            Enabled = true,
            ScheduleCronExpression = null
        };

        // Act
        var next = settings.NextOccurrence;

        // Assert
        Assert.AreEqual(TimeSpan.Zero, next);
    }

    [TestMethod]
    public void NextOccurrence_CronExpressionIsValid_ReturnsFutureDelay()
    {
        // Arrange
        var settings = new TestWorkerSettings
        {
            Enabled = true,
            ScheduleCronExpression = "* * * * * *"
        };

        // Act
        var next = settings.NextOccurrence;

        // Assert
        Assert.IsTrue(next > TimeSpan.Zero, "Should be in the future");
        Assert.IsTrue(next < TimeSpan.FromMinutes(2), "Should be within a minute (and a bit)");
    }

    [TestMethod]
    [DataRow("*/5 * * * *", false)]
    [DataRow("0 */5 * * * *", true)]
    public void ScheduleCronExpression_FiveOrSixFieldSyntax_ValidatesExpected(string schedule, bool expectedIsValid)
    {
        // Arrange
        var settings = new TestWorkerSettings
        {
            ScheduleCronExpression = schedule
        };
        var validationResults = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(
            settings,
            new ValidationContext(settings),
            validationResults,
            validateAllProperties: true);

        // Assert
        Assert.AreEqual(expectedIsValid, isValid);
    }

    [TestMethod]
    public void IdleBackoffDuration_DefaultSettings_ReturnsZero()
    {
        // Arrange & Act
        var settings = new TestWorkerSettings();

        // Assert
        Assert.AreEqual(TimeSpan.Zero, settings.IdleBackoffDuration);
    }

    [TestMethod]
    public void NextOccurrence_CronExpressionIsInvalid_ThrowsCronFormatException()
    {
        // Arrange
        var settings = new TestWorkerSettings
        {
            Enabled = true,
            ScheduleCronExpression = "invalid cron"
        };

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

    [TestMethod]
    public void Settings_DefaultPropertyValues_PassDataAnnotationValidation()
    {
        var settings = new TestWorkerSettings();
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            settings,
            new ValidationContext(settings),
            validationResults,
            validateAllProperties: true);

        Assert.IsTrue(isValid, string.Join(Environment.NewLine, validationResults));
    }

    [TestMethod]
    [DataRow(nameof(WorkerBackgroundServiceSettings.RetryCount), "-1")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.RetryCount), "101")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.RetryBaseDelaySeconds), "0")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.RetryBaseDelaySeconds), "3601")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.RetryMaxDelaySeconds), "0")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.RetryMaxDelaySeconds), "3601")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.HealthSampleSize), "0")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.HealthSampleSize), "1001")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.HealthDegradedThresholdMultiplier), "0.9")]
    [DataRow(nameof(WorkerBackgroundServiceSettings.HealthDegradedThresholdMultiplier), "101")]
    public void Settings_DataAnnotatedPropertyIsOutOfRange_FailsValidation(
        string propertyName,
        string propertyValue)
    {
        var settings = new TestWorkerSettings();
        var property = typeof(WorkerBackgroundServiceSettings).GetProperty(propertyName);
        Assert.IsNotNull(property);
        var value = Convert.ChangeType(propertyValue, property.PropertyType, CultureInfo.InvariantCulture);
        property.SetValue(settings, value);
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            settings,
            new ValidationContext(settings),
            validationResults,
            validateAllProperties: true);

        Assert.IsFalse(isValid);
        Assert.IsTrue(validationResults.Exists(result => result.MemberNames.Contains(propertyName)));
    }

    [TestMethod]
    public void OptionsValidation_DefaultSettings_PassesStartupValidation()
    {
        using var serviceProvider = BuildValidatedOptions();

        serviceProvider.GetRequiredService<IStartupValidator>().Validate();
    }

    [TestMethod]
    [DataRow("retry-max-below-base")]
    [DataRow("zero-health-timeout")]
    [DataRow("zero-shutdown-timeout")]
    [DataRow("negative-continuous-delay")]
    [DataRow("negative-idle-backoff")]
    public void OptionsValidation_InvalidOperationalSetting_ThrowsAtStartup(string scenario)
    {
        using var serviceProvider = BuildValidatedOptions(settings =>
        {
            switch (scenario)
            {
                case "retry-max-below-base":
                    settings.RetryBaseDelaySeconds = 2;
                    settings.RetryMaxDelaySeconds = 1;
                    break;
                case "zero-health-timeout":
                    settings.HealthHardTimeout = TimeSpan.Zero;
                    break;
                case "zero-shutdown-timeout":
                    settings.ShutdownTimeout = TimeSpan.Zero;
                    break;
                case "negative-continuous-delay":
                    settings.DelayBetweenExecutions = TimeSpan.FromSeconds(-1);
                    break;
                case "negative-idle-backoff":
                    settings.IdleBackoffDuration = TimeSpan.FromSeconds(-1);
                    break;
                default:
                    Assert.Fail($"Unknown scenario: {scenario}");
                    break;
            }
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IStartupValidator>().Validate());
    }
}
