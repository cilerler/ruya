# Ruya.Services.DistributedLock

The core library for distributed locking in .NET. It provides the base implementation, telemetry, and health checks for distributed lock providers.

## Features

-   **Robust Locking**: Implements the "acquire, execute, release" pattern safely.
-   **Telemetry**: Built-in OpenTelemetry support for tracking lock acquisition and duration.
-   **Health Checks**: Verifies connectivity to the lock provider.
-   **Extensible**: Easy to add new providers.

## Usage

### Registration

```csharp
builder.Services.AddRedisDistributedLock();
```

### Acquiring a Lock

Inject `IDistributedLock`.

```csharp
public class JobService
{
    private readonly IDistributedLock _lock;

    public JobService(IDistributedLock distLock)
    {
        _lock = distLock;
    }

    public async Task RunJobAsync(CancellationToken cancellationToken)
    {
        var result = await _lock.AcquireAndExecuteWithLockAsync(
            async (ct) => 
            {
                // Critical section
                await ProcessDataAsync(ct);
            },
            "my-job-lock",
            lockValue: null,
            options: new LockOptions 
            { 
                CustomExpiry = TimeSpan.FromSeconds(30),
                HeartbeatInterval = TimeSpan.FromSeconds(10)
            },
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            Console.WriteLine("Could not acquire lock.");
        }
    }
}
```

Ruya always generates a different owner value for every acquisition. An optional `lockValue` is treated only as a diagnostic prefix; Ruya appends a unique acquisition identifier before calling the provider. That prevents a delayed release from an expired operation from releasing a newer lock acquired by the same process.

`AddDistributedLockCore()` binds `DistributedLock` when `IConfiguration` is present. Configuration-free service collections remain supported for programmatic in-memory registration. Calling `AddDistributedLockMetrics("custom-name")` explicitly replaces the default meter registration.
