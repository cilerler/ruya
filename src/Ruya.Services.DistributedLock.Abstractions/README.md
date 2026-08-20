# Ruya.Services.DistributedLock.Abstractions

Core abstractions for the Ruya Distributed Lock framework. It defines a unified interface for managing distributed locks across various providers (Redis, SQL Server, In-Memory).

## Features

-   **Unified Interface**: `IDistributedLock` provides a consistent API.
-   **Safe Execution**: `AcquireAndExecuteWithLockAsync` ensures locks are released automatically.
-   **Options**: Configurable expiry and heartbeat behavior.

## Usage

```csharp
public interface IDistributedLock
{
    Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue = null,
        LockOptions? options = null);

    Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue,
        LockOptions? options,
        CancellationToken cancellationToken);
}
```

Use the token-last overload for cancellation-aware application work. Keeping the token last and required prevents calls that pass positional `default` from becoming ambiguous with the released 8.x overload. The original overload remains available for compatibility. Existing third-party implementations receive a default bridge that links cancellation into their callback; Ruya's implementation additionally cancels provider acquisition.
