# Ruya.Services.DistributedLock.Abstractions

Core abstractions for the Ruya Distributed Lock framework. It defines a unified interface for managing distributed locks across various providers (Redis, SQL Server, In-Memory).

## Features

-   **Unified Interface**: `IDistributedLock` provides a consistent API.
-   **Safe Execution**: `AcquireAndExecuteWithLockAsync` ensures locks are released automatically.
-   **Options**: Configurable timeouts, wait times, and retry policies.

## Usage

```csharp
public interface IDistributedLock
{
    Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue = null,
        LockOptions? options = null);
}
```
