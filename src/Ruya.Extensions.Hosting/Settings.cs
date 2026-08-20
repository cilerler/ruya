using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using Cronos;
using Ruya.Extensions.Hosting.Validators;

namespace Ruya.Extensions.Hosting;

public class WorkerBackgroundServiceSettings
{
    public const string ConfigurationSectionName = nameof(WorkerBackgroundService<WorkerBackgroundServiceSettings>);
    public static readonly string FeatureFlag = ConfigurationSectionName;

    [JsonIgnore]
    public bool Enabled { get; set; }
    public bool RunOnce { get; set; }
    public bool RunImmediately { get; set; }

    [ScheduleValidation(AllowEmpty = true, ErrorMessage = "Invalid schedule expression.")]
    public string? ScheduleCronExpression { get; set; }

    // Retry settings
    public bool RetryEnabled { get; set; } = false;

    [Range(0, 100)]
    public int RetryCount { get; set; } = 3;

    [Range(1, 3600)]
    public int RetryBaseDelaySeconds { get; set; } = 1;

    [Range(1, 3600)]
    public int RetryMaxDelaySeconds { get; set; } = 30;

    // Health settings
    [Range(1, 1000)]
    public int HealthSampleSize { get; set; } = 5;

    [Range(1.0, 100.0)]
    public double HealthDegradedThresholdMultiplier { get; set; } = 2.0;
    public TimeSpan? HealthHardTimeout { get; set; }

    // Shutdown settings
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // Delay settings
    public TimeSpan DelayBetweenExecutions { get; set; } = TimeSpan.Zero;

    // Idle backoff settings
    public TimeSpan IdleBackoffDuration { get; set; } = TimeSpan.Zero;

    public bool RunContinuously => string.IsNullOrWhiteSpace(ScheduleCronExpression);

    public TimeSpan NextOccurrence
    {
        get
        {
            if (!Enabled || RunOnce) return Timeout.InfiniteTimeSpan;
            if (RunContinuously) return TimeSpan.Zero;

            var expression = CronExpression.Parse(ScheduleCronExpression!, CronFormat.IncludeSeconds);
            var next = expression.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            if (next == null) throw new InvalidOperationException("Failed to calculate the next occurrence from the cron expression.");
            return next == DateTimeOffset.MinValue ? Timeout.InfiniteTimeSpan : (DateTimeOffset)next - DateTimeOffset.UtcNow;
        }
    }
}
