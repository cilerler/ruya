# Ruya.Extensions.Hosting

Extensions for hosting background worker services with advanced scheduling and reliability features.

## Features

-   **WorkerBackgroundService**: Abstract base class extending `BackgroundService` with `IHostedLifecycleService` support.
-   **Advanced Scheduling**: Support for Cron expressions via `Cronos`, "Run Once", "Run Immediately", and "Run Continuous" modes.
-   **Idle Backoff**: Configurable delay when no data is found, reducing unnecessary polling without custom logic in derived services.
-   **Resilience**: Built-in exponential backoff with jitter for retrying failed execution attempts.
-   **Observability**: Integrated logic for distributed tracing, metrics (counters, histograms), and health checks.
-   **Graceful Shutdown**: Configurable timeout for graceful shutdowns to prevent data loss.

## Usage

Create a robust background service by inheriting from `WorkerBackgroundService<TSettings>`:

```csharp
public class MyWorkerSettings : WorkerBackgroundServiceSettings
{
    public string TargetUrl { get; set; }
}

public class MyWorkerService : WorkerBackgroundService<MyWorkerSettings>
{
    private readonly HttpClient _httpClient;

    public MyWorkerService(
        ILogger<MyWorkerService> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<MyWorkerSettings> options,
        IEnumerable<IHealthCheck> healthChecks,
        HttpClient httpClient)
        : base(logger, distributedTracing, meterFactory, options, healthChecks)
    {
        _httpClient = httpClient;
    }

    public override async Task DoWorkAsync(CancellationToken cancellationToken)
    {
        var records = await _httpClient.GetAsync(_settings.TargetUrl, cancellationToken);

        // Signal the base class when there's no data to process.
        // If IdleBackoffDuration is configured, the base class will
        // automatically delay before the next execution.
        IdleCycle = records.Content.Headers.ContentLength == 0;
    }
}
```

### Registration

```csharp
// Program.cs
builder.Services.AddHostedService<MyWorkerService>();
builder.Services.Configure<MyWorkerSettings>(builder.Configuration.GetSection("MyWorker"));
```

## Configuration

Configure the execution behavior using standard `appsettings.json`:

```json
{
  "MyWorker": {
    // "RunOnce": false,
    // "RunImmediately": true,
    // Leave Cron empty for continuous execution (loop)
    "ScheduleCronExpression": "*/5 * * * *", 
    
    "RetryEnabled": true,
    "RetryCount": 3,
    "RetryBaseDelaySeconds": 2,

    "ShutdownTimeout": "00:01:00",

    // When IdleCycle is set to true in DoWorkAsync, wait this long before next execution.
    // Set to "00:00:00" (default) to disable.
    "IdleBackoffDuration": "00:00:30",

    "HealthSampleSize": 10,
    "HealthHardTimeout": "00:05:00"
  }
}
```
