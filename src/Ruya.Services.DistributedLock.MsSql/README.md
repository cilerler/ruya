# Ruya.Services.DistributedLock.MsSql

SQL Server provider for `Ruya.Services.DistributedLock`. Uses `sp_getapplock` for reliable, database-level distributed locking.

## Configuration

```csharp
services.AddDistributedLock()
    .AddMsSqlDistributedLock(options =>
    {
        options.ConnectionString = "Server=localhost;Database=MyDb;Integrated Security=true;";
    });
```

## Usage

See [Ruya.Services.DistributedLock](../Ruya.Services.DistributedLock/README.md) for usage examples.
