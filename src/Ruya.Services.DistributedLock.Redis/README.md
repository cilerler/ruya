# Ruya.Services.DistributedLock.Redis

Redis provider for `Ruya.Services.DistributedLock`. Uses Redis `SET NX PX` (or RedLock algorithm if configured) for high-performance distributed locking.

## Configuration

```csharp
builder.Services.AddRedisDistributedLock();
```

Configure the non-secret catalog key at `DistributedLock:Redis:ConnectionStringKey`. Supply the matching `ConnectionStrings` value through an application secret provider; do not commit Redis credentials to normal settings files.

The catalog value is validated during options startup but is not copied into `RedisLockSettings`. It is resolved inside the Redis client factory immediately before the provider uses it. The legacy `ConnectionString` property remains on the settings type for 8.x binary compatibility and is intentionally not populated by key-based registration.

For Redlock, call `AddRedlockDistributedLock()` and configure an odd number of at least three independent connection-string catalog keys at `DistributedLock:Redis:RedlockConnectionStringKeys`. Raw `RedlockEndpoints` remain supported for compatibility, but should only be supplied by a secret provider.

Catalog-based Redlock endpoints are likewise resolved into a local provider-factory value and are never written back into the options instance.

The released programmatic path is also preserved: register a caller-owned `IConnectionMultiplexer` before `AddRedlockDistributedLock()` when one externally managed Redis connection is intentional. The provider uses that instance without disposing it.

## Usage

See [Ruya.Services.DistributedLock](../Ruya.Services.DistributedLock/README.md) for usage examples.
