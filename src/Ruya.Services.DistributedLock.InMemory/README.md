# Ruya.Services.DistributedLock.InMemory

In-Memory provider for `Ruya.Services.DistributedLock`. Uses `SemaphoreSlim` for local process locking. Ideal for testing or single-instance applications.

## Configuration

```csharp
services.AddDistributedLock()
    .AddInMemoryDistributedLock();
```

## Usage

See [Ruya.Services.DistributedLock](../Ruya.Services.DistributedLock/README.md) for usage examples.
