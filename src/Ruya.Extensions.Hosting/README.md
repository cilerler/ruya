# Ruya.Extensions.Hosting

Extensions for hosting background worker services with advanced scheduling and reliability features.

## Features

-   **WorkerBackgroundService**: Abstract base class extending `BackgroundService` with `IHostedLifecycleService` support.
-   **Advanced Scheduling**: Support for Cron expressions via `Cronos`, "Run Once", "Run Immediately", and "Run Continuous" modes.
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
        _logger.LogInformation("Processing work for {Url}", _settings.TargetUrl);
        // Your logic here
        await _httpClient.GetAsync(_settings.TargetUrl, cancellationToken);
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
    "Enabled": true,
    // "RunOnce": false,
    // "RunImmediately": true,
    // Leave Cron empty for continuous execution (loop)
    "ScheduleCronExpression": "*/5 * * * *", 
    
    "RetryEnabled": true,
    "RetryCount": 3,
    "RetryBaseDelaySeconds": 2,

    "ShutdownTimeoutSeconds": 60,
    
    "HealthSampleSize": 10,
    "HealthHardTimeout": "00:05:00"
  }
}
```
