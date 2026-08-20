# Ruya.Services.DistributedLock.InMemory

In-Memory provider for `Ruya.Services.DistributedLock`. Uses atomic operations over a process-local lock table. Ideal for testing or single-instance applications; it does not coordinate separate processes or replicas.

## Configuration

```csharp
builder.Services.AddInMemoryDistributedLock();
```

## Usage

See [Ruya.Services.DistributedLock](../Ruya.Services.DistributedLock/README.md) for usage examples.
