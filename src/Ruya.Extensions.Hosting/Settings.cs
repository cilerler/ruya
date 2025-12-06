using System;
using System.Threading;
using Cronos;
using Ruya.Extensions.Hosting.Validators;

namespace Ruya.Extensions.Hosting;

public class WorkerBackgroundServiceSettings
{
    public const string ConfigurationSectionName = nameof(WorkerBackgroundService<WorkerBackgroundServiceSettings>);
    public static readonly string FeatureFlag = ConfigurationSectionName;

    public bool Enabled { get; internal set; }
    public bool RunOnce { get; set; }
    public bool RunImmediately { get; set; }

    [ScheduleValidation(AllowEmpty = true, ErrorMessage = "Invalid schedule expression.")]
    public string? ScheduleCronExpression { get; set; }

    // Retry settings
    public bool RetryEnabled { get; set; } = false;
    public int RetryCount { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 1;

    // Health settings
    public int HealthSampleSize { get; set; } = 5;
    public double HealthDegradedThresholdMultiplier { get; set; } = 2.0;
    public TimeSpan? HealthHardTimeout { get; set; }

    // Shutdown settings
    public int ShutdownTimeoutSeconds { get; set; } = 30;

    public bool RunContinuously => string.IsNullOrWhiteSpace(ScheduleCronExpression);

    public TimeSpan NextOccurrence
    {
        get
        {
            if (!Enabled || RunOnce) return Timeout.InfiniteTimeSpan;
            if (RunContinuously) return TimeSpan.Zero;

            var expression = CronExpression.Parse(ScheduleCronExpression!);
            var next = expression.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
			if (next == null) throw new InvalidOperationException("Failed to calculate the next occurrence from the cron expression.");
            return next == DateTimeOffset.MinValue ? Timeout.InfiniteTimeSpan : (DateTimeOffset)next - DateTimeOffset.UtcNow;
        }
    }
}
