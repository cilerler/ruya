# Ruya.AspNetCore.DataProtection.StackExchangeRedis

ASP.NET Core Data Protection with a shared Redis key repository. The package supports direct server
configuration and an external-client mode that retrieves Redis settings at runtime.

## Features

- Redis-backed Data Protection key persistence
- Application-name and purpose isolation
- Direct server registration
- Runtime settings bootstrap for external clients such as MAUI applications
- One singleton `IConnectionMultiplexer` exposed through dependency injection
- Redis connectivity and protect/unprotect health checks
- OpenTelemetry-compatible logs, traces, and metrics

## Server mode

Keep non-secret settings in ordinary configuration:

```json
{
  "DataProtectionSettings": {
    "ApplicationName": "MyApplication",
    "ConnectionStringKey": "Redis",
    "CacheKey": "DataProtection-Keys",
    "DefaultKeyLifetime": 90
  }
}
```

Supply the referenced Redis credential through user-secrets for local development and through an
environment variable or deployed secret provider elsewhere:

```powershell
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379,password=<local-password>,abortConnect=false"
$env:ConnectionStrings__Redis = "redis.internal:6379,password=<deployment-secret>,ssl=true"
```

Register the service:

```csharp
builder.Services.AddMetrics();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddDistributedTracingService();
builder.Services.AddDataProtectionServer(settings =>
{
    settings.Purposes.Add(
        DataProtectionService.DefaultPurpose,
        "MyApplication.Encryption");
});
```

## Remote client mode

Remote client mode is intended for external applications that must not embed the Redis credential in
their distributed application package. The application downloads `DataProtectionSettings` from an
authenticated server endpoint at runtime and then opens one direct Redis connection.

Runtime retrieval prevents static credential embedding; it does **not** hide the credential from the
client process or from a compromised device. Use authenticated HTTPS, a restricted Redis ACL identity,
TLS, and an endpoint authorization policy appropriate for trusted clients.

The settings endpoint must stay on the configured service origin. HTTPS is required except for an HTTP
loopback address used during local development; endpoint user-info credentials and cross-origin absolute
URLs are rejected during options validation.

### Server endpoint

The settings server can expose its resolved settings through an authenticated endpoint:

```csharp
app.MapGet(
        "/api/DataProtection",
        (IOptions<DataProtectionSettings> settings) => Results.Json(settings.Value))
    .RequireAuthorization("DataProtectionClients");
```

Do not expose this endpoint anonymously. Its response intentionally includes the resolved Redis
connection string required by the external client.

### Client configuration

The client stores only the settings-service address:

```json
{
  "ConnectionStrings": {
    "ConfigService": "https://config.example.com"
  },
  "DataProtectionClientSettings": {
    "ConnectionStringKey": "ConfigService",
    "Endpoint": "/api/DataProtection"
  }
}
```

Configure authentication on the package's named HTTP client, then register remote Data Protection:

```csharp
builder.Services.AddHttpClient(
    DataProtectionClientSettings.HttpClientName,
    client => client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", deviceAccessToken));

builder.Services.AddMetrics();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddDistributedTracingService();
builder.Services.AddDataProtectionClient("MyApplication.Encryption");
```

The released `IDataProtection` and `IConnectionMultiplexer` contracts are synchronous. To keep their
first use from performing the remote bootstrap synchronously, initialize the client from an async
application-startup or MAUI lifecycle boundary before resolving Data Protection:

```csharp
await app.Services.InitializeDataProtectionClientAsync(cancellationToken);
```

For compatibility, first resolution still initializes the client if this explicit prewarming step is
not used. A failed bootstrap attempt is not cached permanently; the next initialization or readiness
attempt retries it.

## Sharing the Redis connection

`AddDataProtectionServer` and `AddDataProtectionClient` register one singleton
`IConnectionMultiplexer`. StackExchange.Redis multiplexers are designed to be long-lived and reused.

Register Data Protection before Redis Distributed Lock so the latter inherits the existing connection
and does not require another local Redis credential:

```csharp
builder.Services.AddDataProtectionClient("MyApplication.Encryption");
builder.Services.AddRedisDistributedLock();
```

Microsoft's StackExchange Redis implementation of `IDistributedCache` does not automatically resolve an
`IConnectionMultiplexer` from dependency injection. It can be pointed at the shared instance explicitly:

```csharp
builder.Services.AddStackExchangeRedisCache(_ => { });
builder.Services.AddOptions<RedisCacheOptions>()
    .Configure<IConnectionMultiplexer>((options, connection) =>
    {
        options.ConnectionMultiplexerFactory =
            () => Task.FromResult(connection);
    });
```

`RedisCache` treats a factory-returned connection as owned and may close it during disposal or forced
reconnect. Use this compatibility pattern only when the cache and root service provider have the same
lifetime and that ownership behavior is acceptable. Otherwise give `IDistributedCache` a dedicated
multiplexer.

The Redis MessageQueue provider currently owns its own connection and does not inherit this singleton.

## Using the service

```csharp
public sealed class MyService(IDataProtection dataProtection)
{
    public string Encrypt(string plainText) => dataProtection.Protect(plainText);

    public string Decrypt(string protectedText) => dataProtection.Unprotect(protectedText);
}
```

## Configuration reference

### `DataProtectionSettings`

| Property | Required | Description |
|---|---:|---|
| `ApplicationName` | Yes | Isolates keys for the consuming application. |
| `ConnectionStringKey` | Yes | Name of the server's `ConnectionStrings` catalog entry. |
| `CacheKey` | Yes | Redis key that stores the Data Protection key ring. |
| `DefaultKeyLifetime` | No | Key lifetime in days, from 1 through 365. Default: 90. |
| `ConnectionString` | Runtime | Resolved locally in server mode or delivered to a remote client. Never persist or log it. |

### `DataProtectionClientSettings`

| Property | Required | Description |
|---|---:|---|
| `ConnectionStringKey` | Yes | Name of the local connection-string entry containing the settings-service base address. |
| `Endpoint` | Yes | Relative or same-origin absolute settings endpoint. |

## Health check

Both registration modes add `dataprotection-redis`. In remote mode the health check awaits client
initialization, then verifies Redis connectivity and a local protect/unprotect round trip. Map it through
the consuming application's canonical readiness endpoint.

## Metrics

| Metric | Type | Description |
|---|---|---|
| `dataprotection.protect.operations` | Counter | Protect operations. |
| `dataprotection.unprotect.operations` | Counter | Unprotect operations. |
| `dataprotection.protect.failures` | Counter | Failed protect operations. |
| `dataprotection.unprotect.failures` | Counter | Failed unprotect operations. |
| `dataprotection.operation.duration` | Histogram | Operation duration in seconds. |
