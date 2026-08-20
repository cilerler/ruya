# Ruya.Services.DistributedLock.MsSql

SQL Server provider for `Ruya.Services.DistributedLock`. Uses `sp_getapplock` for reliable, database-level distributed locking.

## Configuration

```csharp
builder.Services.AddSqlServerDistributedLock();
```

Configure the non-secret catalog key at `DistributedLock:SqlServer:ConnectionStringKey`. Supply the matching `ConnectionStrings` value through an application secret provider; do not commit database credentials to normal settings files.

The catalog value is validated during options startup but is not copied into `SqlServerLockSettings`. It is resolved inside the provider factory immediately before use. The legacy `ConnectionString` property remains for 8.x binary compatibility and is intentionally not populated by key-based registration.

Each acquired application lock retains its owning SQL session. Heartbeats verify `APPLOCK_MODE` through that same connection before extending local expiry, and extension, release, and timer expiry are serialized for that acquisition. Lock sessions deliberately disable SQL connection pooling, so an unconfirmed release can close the physical session without clearing or reusing the application's normal connection pool.

## Usage

See [Ruya.Services.DistributedLock](../Ruya.Services.DistributedLock/README.md) for usage examples.
