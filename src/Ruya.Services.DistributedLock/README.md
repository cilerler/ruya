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
// In Startup.cs
services.AddDistributedLock(options =>
{
    options.DefaultProvider = "redis";
})
.AddRedisDistributedLock(options =>
{
    options.ConnectionString = "localhost:6379";
});
```

### Acquiring a Lock

Inject `IDistributedLock` (or `IDistributedLockFactory` if using multiple providers).

```csharp
public class JobService
{
    private readonly IDistributedLock _lock;

    public JobService(IDistributedLock distLock)
    {
        _lock = distLock;
    }

    public async Task RunJobAsync()
    {
        var result = await _lock.AcquireAndExecuteWithLockAsync(
            async (ct) => 
            {
                // Critical section
                await ProcessDataAsync();
            },
            "my-job-lock",
            options: new LockOptions 
            { 
                Expiry = TimeSpan.FromSeconds(30),
                Wait = TimeSpan.FromSeconds(5)
            });

        if (!result.Acquired)
        {
            Console.WriteLine("Could not acquire lock.");
        }
    }
}
```
