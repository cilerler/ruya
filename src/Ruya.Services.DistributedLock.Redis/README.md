# Ruya.Services.DistributedLock.Redis

Redis provider for `Ruya.Services.DistributedLock`. Uses Redis `SET NX PX` (or RedLock algorithm if configured) for high-performance distributed locking.

## Configuration

```csharp
services.AddDistributedLock()
    .AddRedisDistributedLock(options =>
    {
        options.ConnectionString = "localhost:6379";
        options.InstanceName = "MyApp:";
    });
```

## Usage

See [Ruya.Services.DistributedLock](../Ruya.Services.DistributedLock/README.md) for usage examples.
