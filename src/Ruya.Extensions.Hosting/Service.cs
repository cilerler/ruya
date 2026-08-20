using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Primitives;
using static Ruya.Extensions.Hosting.WorkerBackgroundServiceEventIds;

namespace Ruya.Extensions.Hosting;

internal static class WorkerBackgroundServiceEventIds
{
    internal static readonly EventId StartupValidationSkipped = new(1000, nameof(StartupValidationSkipped));
    internal static readonly EventId StartupValidationStarting = new(1001, nameof(StartupValidationStarting));
    internal static readonly EventId StartupValidationCompleted = new(1002, nameof(StartupValidationCompleted));
    internal static readonly EventId ServiceDisabled = new(1003, nameof(ServiceDisabled));
    internal static readonly EventId ShutdownStarting = new(1004, nameof(ShutdownStarting));
    internal static readonly EventId ShutdownCompleted = new(1005, nameof(ShutdownCompleted));
    internal static readonly EventId ShutdownHostCancelled = new(1006, nameof(ShutdownHostCancelled));
    internal static readonly EventId ShutdownTimedOut = new(1007, nameof(ShutdownTimedOut));
    internal static readonly EventId ShutdownCancelled = new(1008, nameof(ShutdownCancelled));
    internal static readonly EventId ShutdownFailed = new(1009, nameof(ShutdownFailed));
    internal static readonly EventId ServiceStopped = new(1010, nameof(ServiceStopped));
    internal static readonly EventId ExecutionModeSelected = new(1011, nameof(ExecutionModeSelected));
    internal static readonly EventId InitialExecutionSkipped = new(1012, nameof(InitialExecutionSkipped));
    internal static readonly EventId RunOnceCompleted = new(1013, nameof(RunOnceCompleted));
    internal static readonly EventId LoopDelayStarting = new(1014, nameof(LoopDelayStarting));
    internal static readonly EventId ScheduleCompleted = new(1015, nameof(ScheduleCompleted));
    internal static readonly EventId ScheduleDelayStarting = new(1016, nameof(ScheduleDelayStarting));
    internal static readonly EventId ExecutionStarting = new(1017, nameof(ExecutionStarting));
    internal static readonly EventId ExecutionCompleted = new(1018, nameof(ExecutionCompleted));
    internal static readonly EventId ExecutionCancelled = new(1019, nameof(ExecutionCancelled));
    internal static readonly EventId ExecutionFailed = new(1020, nameof(ExecutionFailed));
    internal static readonly EventId ExecutionRetrying = new(1021, nameof(ExecutionRetrying));
}

public abstract class WorkerBackgroundService<TSettings> : IHostedLifecycleService, IDisposable
    where TSettings : WorkerBackgroundServiceSettings
{
#pragma warning disable IDE1006
    protected readonly ILogger _logger;
    protected readonly IDistributedTracing _tracer;
    protected readonly Meter _meter;
    protected readonly TSettings _settings;
#pragma warning restore IDE1006

    private readonly HealthCheckService _healthCheckService;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly object _statisticsLock = new();

    // Health tracking (thread-safe via _statisticsLock)
    private readonly Queue<double> _executionDurations = new();
    private double _lastExecutionDuration;
    private DateTimeOffset _lastSuccessfulCompletion = DateTimeOffset.UtcNow;

    // Metrics
    private readonly UpDownCounter<int> _activeExecutions;
    private readonly Counter<long> _executionTotal;
    private readonly Counter<long> _executionSuccess;
    private readonly Counter<long> _executionFailed;
    private readonly Counter<long> _retryTotal;
    private readonly Histogram<double> _executionDuration;

    private Task? _executingTask;

    protected WorkerBackgroundService(
        ILogger<WorkerBackgroundService<TSettings>> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<TSettings> options,
        HealthCheckService healthCheckService,
        IHostApplicationLifetime hostApplicationLifetime)
    {
        _logger = logger;
        _tracer = distributedTracing;
        _meter = meterFactory.Create(new MeterOptions(Startup.AssemblyName)
        {
            Version = Startup.AssemblyVersion,
            Tags = new TagList
            {
                { "code.namespace", GetType().Namespace },
                { "code.class", GetType().Name }
            }
        });
        _settings = options.Value;
        _healthCheckService = healthCheckService;
        _hostApplicationLifetime = hostApplicationLifetime;

        var serviceName = JsonNamingPolicy.SnakeCaseLower.ConvertName(GetType().Name);
        _activeExecutions = _meter.CreateUpDownCounter<int>(
            $"app_{serviceName}_active", "executions", "Currently active executions across instances");
        _executionTotal = _meter.CreateCounter<long>(
            $"app_{serviceName}_total", "executions", "Total execution attempts");
        _executionSuccess = _meter.CreateCounter<long>(
            $"app_{serviceName}_success", "executions", "Successful executions");
        _executionFailed = _meter.CreateCounter<long>(
            $"app_{serviceName}_failed", "executions", "Failed executions");
        _retryTotal = _meter.CreateCounter<long>(
            $"app_{serviceName}_retries", "retries", "Total retry attempts");
        _executionDuration = _meter.CreateHistogram<double>(
            $"app_{serviceName}_duration_seconds", "s", "Execution duration");
    }

    protected bool IdleCycle { get; set; }

    public abstract Task DoWorkAsync(CancellationToken cancellationToken);

    protected abstract bool IsTransient(Exception exception);

    #region IHostedLifecycleService

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug(
                StartupValidationSkipped,
                "Service {ServiceName} is disabled. Skipping startup validation.",
                GetType().Name);
            return;
        }

        _logger.LogDebug(StartupValidationStarting, "Service starting. Validating dependencies.");

        var result = await _healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("startup", StringComparer.Ordinal),
            cancellationToken);

        if (result.Status != HealthStatus.Healthy)
        {
            var failedChecks = string.Join(
                ", ",
                result.Entries
                    .Where(entry => entry.Value.Status != HealthStatus.Healthy)
                    .Select(entry => $"{entry.Key}={entry.Value.Status}"));
            throw new InvalidOperationException(
                $"Startup dependency health checks failed: {failedChecks}.");
        }

        _logger.LogDebug(StartupValidationCompleted, "All startup dependency health checks passed.");
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation(ServiceDisabled, "Service {ServiceName} is disabled.", GetType().Name);
            return Task.CompletedTask;
        }

        _executingTask = RunScheduleLoopAsync(_cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(ShutdownStarting, "Host shutdown requested. Initiating graceful shutdown.");
        await _cancellationTokenSource.CancelAsync();

        var executingTask = _executingTask;
        if (executingTask is null)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_settings.ShutdownTimeout);

        try
        {
            await executingTask.WaitAsync(timeoutCts.Token);
            _logger.LogInformation(ShutdownCompleted, "Work completed gracefully.");
        }
        catch (OperationCanceledException) when (!executingTask.IsCompleted && cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ShutdownHostCancelled,
                "Host shutdown cancellation was requested before work completed.");
        }
        catch (OperationCanceledException) when (!executingTask.IsCompleted && timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                ShutdownTimedOut,
                "Shutdown timeout ({ShutdownTimeout}) exceeded. Work may be incomplete.",
                _settings.ShutdownTimeout);
        }
        catch (OperationCanceledException) when (executingTask.IsCanceled)
        {
            _logger.LogInformation(ShutdownCancelled, "Shutdown completed via cancellation.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ShutdownFailed, ex, "Error during shutdown.");
            throw;
        }
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(ServiceStopped, "Service stopped.");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion

    #region Execution

    private async Task RunScheduleLoopAsync(CancellationToken cancellationToken)
    {
        // Yield to ensure the loop runs asynchronously and doesn't block StartedAsync
        await Task.Yield();

        var mode = _settings.RunContinuously ? "continuous" : $"schedule: {_settings.ScheduleCronExpression}";
        _logger.LogInformation(ExecutionModeSelected, "Service running in {Mode} mode.", mode);

        var isFirstExecution = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            var shouldExecute = _settings.RunOnce || !isFirstExecution || _settings.RunImmediately || _settings.RunContinuously;
            if (shouldExecute)
            {
                IdleCycle = false;
                await ExecuteWorkAsync(cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    InitialExecutionSkipped,
                    "Skipping initial execution (RunImmediately=false).");
            }

            isFirstExecution = false;

            if (cancellationToken.IsCancellationRequested) break;

            if (_settings.RunOnce)
            {
                _logger.LogInformation(
                    RunOnceCompleted,
                    "Run-once execution completed. No further executions scheduled.");
                break;
            }

            if (_settings.RunContinuously)
            {
                var loopDelay = IdleCycle && _settings.IdleBackoffDuration > TimeSpan.Zero
                    ? _settings.IdleBackoffDuration
                    : _settings.DelayBetweenExecutions;

                if (loopDelay > TimeSpan.Zero)
                {
                    _logger.LogDebug(
                        LoopDelayStarting,
                        "Waiting {Duration} before next continuous execution. Idle cycle: {IdleCycle}.",
                        loopDelay,
                        IdleCycle);
                    try
                    {
                        await Task.Delay(loopDelay, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }

                continue;
            }

            if (cancellationToken.IsCancellationRequested) break;

            // Cron owns the scheduled delay. DelayBetweenExecutions applies only to continuous polling.
            var delay = _settings.NextOccurrence;
            if (delay == Timeout.InfiniteTimeSpan)
            {
                _logger.LogInformation(ScheduleCompleted, "No further executions scheduled.");
                break;
            }

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation(ScheduleDelayStarting, "Next execution in {Delay}.", delay);
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ExecuteWorkAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _activeExecutions.Add(1);
        _executionTotal.Add(1);

        try
        {
            using (_logger.BeginScope("{ExecutionId}", Guid.NewGuid()))
            {
                _logger.LogDebug(ExecutionStarting, "Starting execution.");

                await ExecuteWithRetryAsync(cancellationToken);

                stopwatch.Stop();
                RecordSuccess(stopwatch.Elapsed.TotalSeconds);
                _logger.LogDebug(
                    ExecutionCompleted,
                    "Execution completed in {Duration:F2}s.",
                    stopwatch.Elapsed.TotalSeconds);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ExecutionCancelled, "Execution cancelled.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordFailure(stopwatch.Elapsed.TotalSeconds);
            _logger.LogError(ExecutionFailed, ex, "Execution failed after retries.");
            _hostApplicationLifetime.StopApplication();
            throw;
        }
        finally
        {
            _activeExecutions.Add(-1);
        }
    }

    private async Task ExecuteWithRetryAsync(CancellationToken cancellationToken)
    {
        var maxAttempts = _settings.RetryEnabled ? _settings.RetryCount + 1 : 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await DoWorkAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                _retryTotal.Add(1);
                var delay = CalculateBackoffWithJitter(attempt);
                _logger.LogWarning(
                    ExecutionRetrying,
                    ex,
                    "Attempt {Attempt}/{Max} failed. Retrying in {DelayMs}ms.",
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private TimeSpan CalculateBackoffWithJitter(int attempt)
    {
        const double JitterFactor = 0.5;
        var exponentialDelay = _settings.RetryBaseDelaySeconds * Math.Pow(2, attempt - 1);
        var cappedDelay = Math.Min(_settings.RetryMaxDelaySeconds, exponentialDelay);
        var jitterCapacity = _settings.RetryMaxDelaySeconds - cappedDelay;
        var jitter = Random.Shared.NextDouble() * Math.Min(jitterCapacity, JitterFactor * cappedDelay);
        return TimeSpan.FromSeconds(cappedDelay + jitter);
    }

    #endregion

    #region Health Tracking

    private void RecordSuccess(double elapsedSeconds)
    {
        _executionSuccess.Add(1);
        _executionDuration.Record(elapsedSeconds);

        lock (_statisticsLock)
        {
            _lastExecutionDuration = elapsedSeconds;
            _executionDurations.Enqueue(elapsedSeconds);
            while (_executionDurations.Count > _settings.HealthSampleSize)
            {
                _executionDurations.Dequeue();
            }
            _lastSuccessfulCompletion = DateTimeOffset.UtcNow;
        }
    }

    private void RecordFailure(double elapsedSeconds)
    {
        _executionFailed.Add(1);
        _executionDuration.Record(elapsedSeconds);

        lock (_statisticsLock)
        {
            _lastExecutionDuration = elapsedSeconds;
        }
    }

    public double? GetAverageExecutionDuration()
    {
        lock (_statisticsLock)
        {
            return _executionDurations.Count > 0 ? _executionDurations.Average() : null;
        }
    }

    public double GetLastExecutionDuration()
    {
        lock (_statisticsLock)
        {
            return _lastExecutionDuration;
        }
    }

    public DateTimeOffset GetLastSuccessfulCompletion()
    {
        lock (_statisticsLock)
        {
            return _lastSuccessfulCompletion;
        }
    }

    #endregion

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
        _meter.Dispose();
        GC.SuppressFinalize(this);
    }
}
