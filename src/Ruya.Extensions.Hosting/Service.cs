using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Primitives;

namespace Ruya.Extensions.Hosting;

public abstract class WorkerBackgroundService<TSettings> : IHostedLifecycleService, IDisposable
    where TSettings : WorkerBackgroundServiceSettings
{
#pragma warning disable IDE1006
    protected readonly ILogger _logger;
    protected readonly IDistributedTracing _tracer;
    protected readonly Meter _meter;
    protected readonly TSettings _settings;
#pragma warning restore IDE1006

    private readonly IEnumerable<IHealthCheck> _healthChecks;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private readonly object _statisticsLock = new ();

    // Health tracking (thread-safe via _statisticsLock)
    private readonly Queue<double> _executionDurations = new();
    private double _lastExecutionDuration;
    private DateTime _lastSuccessfulCompletion = DateTime.UtcNow;

    // Metrics
    private readonly UpDownCounter<int> _activeExecutions;
    private readonly Counter<long> _executionTotal;
    private readonly Counter<long> _executionSuccess;
    private readonly Counter<long> _executionFailed;
    private readonly Counter<long> _executionSkipped;
    private readonly Counter<long> _retryTotal;
    private readonly Histogram<double> _executionDuration;

    private Task? _executingTask;

    protected WorkerBackgroundService(
        ILogger<WorkerBackgroundService<TSettings>> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<TSettings> options,
        IEnumerable<IHealthCheck> healthChecks)
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
        _healthChecks = healthChecks;

        var serviceName = JsonNamingPolicy.SnakeCaseLower.ConvertName(GetType().Name);
        _activeExecutions = _meter.CreateUpDownCounter<int>(
            $"app_{serviceName}_active", "executions", "Currently active executions across instances");
        _executionTotal = _meter.CreateCounter<long>(
            $"app_{serviceName}_total", "executions", "Total execution attempts");
        _executionSuccess = _meter.CreateCounter<long>(
            $"app_{serviceName}_success", "executions", "Successful executions");
        _executionFailed = _meter.CreateCounter<long>(
            $"app_{serviceName}_failed", "executions", "Failed executions");
        _executionSkipped = _meter.CreateCounter<long>(
            $"app_{serviceName}_skipped", "executions", "Skipped (previous still running)");
        _retryTotal = _meter.CreateCounter<long>(
            $"app_{serviceName}_retries", "retries", "Total retry attempts");
        _executionDuration = _meter.CreateHistogram<double>(
            $"app_{serviceName}_duration_seconds", "s", "Execution duration");
    }

    protected bool IdleCycle { get; set; }

    public abstract Task DoWorkAsync(CancellationToken cancellationToken);

    #region IHostedLifecycleService

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Service starting. Validating dependencies.");

        foreach (var check in _healthChecks)
        {
            var result = await check.CheckHealthAsync(new HealthCheckContext(), cancellationToken);
            if (result.Status == HealthStatus.Unhealthy)
            {
                throw new InvalidOperationException($"Startup health check failed: {result.Description}");
            }
        }

        _logger.LogDebug("All health checks passed.");
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Service {ServiceName} is disabled.", GetType().Name);
            return Task.CompletedTask;
        }

        _executingTask = RunScheduleLoopAsync(_cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SIGTERM received. Initiating graceful shutdown.");
        await _cancellationTokenSource.CancelAsync();

        if (_executingTask is null)
        {
            return;
        }

        using var timeoutCts = new CancellationTokenSource(_settings.ShutdownTimeout);

        try
        {
            var completedTask = await Task.WhenAny(_executingTask, Task.Delay(_settings.ShutdownTimeout, CancellationToken.None));

            if (completedTask == _executingTask)
            {
                await _executingTask; // Propagate exceptions if any
                _logger.LogInformation("Work completed gracefully.");
            }
            else
            {
                _logger.LogWarning(
                    "Shutdown timeout ({ShutdownTimeout}) exceeded. Work may be incomplete.",
                    _settings.ShutdownTimeout);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Shutdown completed via cancellation.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shutdown.");
        }
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service stopped.");
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
        _logger.LogInformation("Service running in {Mode} mode.", mode);

        var isFirstExecution = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            var shouldExecute = !isFirstExecution || _settings.RunImmediately || _settings.RunContinuously;
            if (shouldExecute)
            {
                IdleCycle = false;
                await ExecuteWorkAsync(cancellationToken);
            }
            else
            {
                _logger.LogInformation("Skipping initial execution (RunImmediately=false).");
            }

            isFirstExecution = false;

            if (cancellationToken.IsCancellationRequested) break;

            // Apply idle backoff if no data was found
            if (IdleCycle && _settings.IdleBackoffDuration > TimeSpan.Zero)
            {
                _logger.LogDebug("Idle cycle detected. Backing off for {Duration}.", _settings.IdleBackoffDuration);
                try
                {
                    await Task.Delay(_settings.IdleBackoffDuration, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested) break;

            // Apply artificial delay between executions if configured
            if (_settings.DelayBetweenExecutions > TimeSpan.Zero)
            {
                _logger.LogDebug("Waiting {Delay} before next execution.", _settings.DelayBetweenExecutions);
                try
                {
                    await Task.Delay(_settings.DelayBetweenExecutions, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested) break;

            var delay = _settings.NextOccurrence;
            if (delay == Timeout.InfiniteTimeSpan)
            {
                _logger.LogInformation("No further executions scheduled.");
                break;
            }

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Next execution in {Delay}.", delay);
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
        if (!await _executionLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Skipping execution - previous run still in progress.");
            _executionSkipped.Add(1);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _activeExecutions.Add(1);
        _executionTotal.Add(1);

        try
        {
            using (_logger.BeginScope("{ExecutionId}", Guid.NewGuid()))
            {
                _logger.LogDebug("Starting execution.");

                await ExecuteWithRetryAsync(cancellationToken);

                stopwatch.Stop();
                RecordSuccess(stopwatch.Elapsed.TotalSeconds);
                _logger.LogDebug("Execution completed in {Duration:F2}s.", stopwatch.Elapsed.TotalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Execution cancelled.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordFailure(stopwatch.Elapsed.TotalSeconds);
            _logger.LogError(ex, "Execution failed after retries.");
        }
        finally
        {
            _activeExecutions.Add(-1);
            _executionLock.Release();
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _retryTotal.Add(1);
                var delay = CalculateBackoffWithJitter(attempt);
                _logger.LogWarning(
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
        var baseDelay = _settings.RetryBaseDelaySeconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * JitterFactor * baseDelay;
        return TimeSpan.FromSeconds(baseDelay + jitter);
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
            _lastSuccessfulCompletion = DateTime.UtcNow;
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

    public DateTime GetLastSuccessfulCompletion()
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
        _executionLock.Dispose();
		_meter.Dispose();
        GC.SuppressFinalize(this);
    }
}
