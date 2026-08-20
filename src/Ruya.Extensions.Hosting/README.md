# Ruya.Extensions.Hosting

Extensions for hosting background worker services with advanced scheduling and reliability features.

## Features

-   **WorkerBackgroundService**: Abstract base class implementing `IHostedLifecycleService` directly.
-   **Advanced Scheduling**: Support for six-field Cronos expressions (including seconds), "Run Once", "Run Immediately", and continuous polling modes.
-   **Idle Backoff**: Configurable delay when no data is found, reducing unnecessary polling without custom logic in derived services.
-   **Resilience**: Opt-in, bounded exponential backoff with jitter for failures that a derived worker explicitly classifies as transient.
-   **Observability**: Execution metrics and health statistics, plus protected tracing and meter facilities for derived workers.
-   **Graceful Shutdown**: Host-aware cancellation with a configurable bound on how long shutdown waits for incomplete work.
-   **Fail-fast terminal behavior**: Non-transient and retry-exhausted failures are recorded, request application shutdown, and remain observable to the host.

## Usage

Keep the worker as a thin scheduling adapter and put business logic in a separate service. The complete generated-service shape is defined by the [background-service reference](https://github.com/cilerler/lillian/blob/main/.github/skills/dotnet-service-generator/references/background-service.md).

```csharp
public sealed class FeedPollingSettings : WorkerBackgroundServiceSettings
{
    public new const string ConfigurationSectionName = "FeedPolling";
    public new static readonly string FeatureFlag = ConfigurationSectionName;

    [Required]
    public required string TargetUrl { get; set; }
}

public interface IFeedPolling
{
    Task<bool> ProcessAsync(CancellationToken cancellationToken);
}

public sealed class FeedPollingService : IFeedPolling
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FeedPollingSettings _settings;

    public FeedPollingService(
        IHttpClientFactory httpClientFactory,
        IOptions<FeedPollingSettings> options)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
    }

    public async Task<bool> ProcessAsync(CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(nameof(FeedPollingWorker));
        using var response = await httpClient.GetAsync(_settings.TargetUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Content.Headers.ContentLength is > 0;
    }
}

public sealed class FeedPollingWorker : WorkerBackgroundService<FeedPollingSettings>
{
    private readonly IFeedPolling _service;

    public FeedPollingWorker(
        ILogger<FeedPollingWorker> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<FeedPollingSettings> options,
        HealthCheckService healthCheckService,
        IHostApplicationLifetime hostApplicationLifetime,
        IFeedPolling service)
        : base(
            logger,
            distributedTracing,
            meterFactory,
            options,
            healthCheckService,
            hostApplicationLifetime)
    {
        _service = service;
    }

    public override async Task DoWorkAsync(CancellationToken cancellationToken)
    {
        var hasData = await _service.ProcessAsync(cancellationToken);
        IdleCycle = !hasData;
    }

    protected override bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TimeoutException or TaskCanceledException;
}
```

### Registration

```csharp
// Program.cs
builder.Services.AddOptions<FeedPollingSettings>()
    .BindConfiguration(FeedPollingSettings.ConfigurationSectionName)
    .Configure<IConfiguration>((settings, configuration) =>
    {
        settings.Enabled = configuration.GetFeatureFlag<FeedPollingSettings>();
    })
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

builder.Services.AddHttpClient(nameof(FeedPollingWorker));
builder.Services.AddSingleton<IFeedPolling, FeedPollingService>();
builder.Services.AddSingleton<FeedPollingWorker>();
builder.Services.AddHostedService(
    serviceProvider => serviceProvider.GetRequiredService<FeedPollingWorker>());
builder.Services.AddHealthChecks();
```

`[JsonIgnore]` affects System.Text.Json only; `IConfigurationBinder` still binds the public `Enabled` setter when that key exists in the worker section. In the registration above, the later `Configure<IConfiguration>` callback deliberately overwrites any bound `Enabled` value with the feature-flag result, so the feature flag remains the single runtime authority.

Disabled workers skip both startup dependency validation and execution. Enabled workers execute only health registrations tagged `startup` before starting; degraded and unhealthy results fail startup. A worker-specific readiness check should use only the `ready` tag so startup validation does not resolve the worker through its own health check.

Register each confirmed dependency health check supplied by its integration with the tags `startup` and `ready`. Do not invent a check type or add a side-effecting probe solely to satisfy startup validation.

`RunOnce` executes exactly once immediately, regardless of `RunImmediately`. Otherwise, `RunImmediately` controls whether a scheduled worker executes before its first scheduled occurrence. Executions are sequential: the next cycle is not scheduled until the current one completes. Scheduled mode waits only for the next cron occurrence. Continuous mode applies `IdleBackoffDuration` after an idle cycle when configured; otherwise it applies `DelayBetweenExecutions`. Those delays are alternatives and are never stacked.

The example registers its business service as a singleton because the worker consumes it through constructor injection. If business logic requires scoped dependencies such as a `DbContext`, inject `IServiceScopeFactory` into the worker and resolve the scoped business service from a new scope for each execution.

## Configuration

Configure the execution behavior using standard `appsettings.json`:

```jsonc
{
  "FeatureManagement": {
    "FeedPolling": true
  },
  "FeedPolling": {
    "TargetUrl": "https://example.test/feed",
    "RunOnce": false,
    "RunImmediately": true,
    // Cronos IncludeSeconds format: second minute hour day month day-of-week.
    // Leave empty for continuous polling and configure DelayBetweenExecutions.
    "ScheduleCronExpression": "0 */5 * * * *",
    "RetryEnabled": true,
    "RetryCount": 3,
    "RetryBaseDelaySeconds": 2,
    "RetryMaxDelaySeconds": 30,
    "ShutdownTimeout": "00:01:00",
    "DelayBetweenExecutions": "00:00:00",
    // In continuous mode, when IdleCycle is true, wait this long before next execution.
    // Set to "00:00:00" (default) to disable.
    "IdleBackoffDuration": "00:00:30",
    "HealthSampleSize": 10,
    "HealthDegradedThresholdMultiplier": 2.0,
    "HealthHardTimeout": "00:05:00"
  }
}
```

`RetryCount` is the number of retries after the initial attempt. Retries occur only when `IsTransient` returns `true`, and every computed retry delay is capped by `RetryMaxDelaySeconds`. In continuous mode, a zero `DelayBetweenExecutions` allows immediate non-idle iterations; configure a non-zero delay unless a measured use case requires a tight loop.
