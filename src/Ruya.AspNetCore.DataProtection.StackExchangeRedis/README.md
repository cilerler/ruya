# Ruya.AspNetCore.DataProtection.StackExchangeRedis

Data protection service with Redis key persistence for ASP.NET Core applications.

## Features

- ASP.NET Core Data Protection with Redis key storage
- Distributed tracing and metrics via OpenTelemetry
- Health checks for Redis connectivity and data protection roundtrip
- Client mode for fetching settings from a remote configuration endpoint

## Installation

```bash
dotnet add package Ruya.AspNetCore.DataProtection.StackExchangeRedis
```

## Usage

### Server Mode (Direct Redis Connection)

Use this mode when your application has direct access to Redis configuration and serves as the configuration source for clients.

**appsettings.json:**

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379,abortConnect=false"
  },
  "DataProtectionSettings": {
    "ApplicationName": "MyApplication",
    "ConnectionStringKey": "Redis",
    "CacheKey": "DataProtection-Keys",
    "DefaultKeyLifetime": 90
  }
}
```

**Program.cs:**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ruya.AspNetCore.DataProtection.StackExchangeRedis;

var builder = WebApplication.CreateBuilder(args);

// Add data protection with Redis
builder.Services.AddDataProtectionServer();

// Add distributed tracing (required dependency)
builder.Services.AddDistributedTracingService();

var app = builder.Build();

// Expose settings endpoint for clients to fetch configuration
app.MapDataProtectionApi();

// Health checks are automatically registered
app.MapHealthChecks("/health");

app.Run();
```

**Extension method for exposing settings API:**

```csharp
public static WebApplication MapDataProtectionApi(this WebApplication app)
{
    app.MapGet("/api/DataProtection", ([FromServices] IOptions<DataProtectionSettings> options) =>
    {
        try
        {
            return Results.Json(options.Value);
        }
        catch (Exception e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .Produces<DataProtectionSettings>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .WithTags("DataProtection");

    return app;
}
```

> **Note:** The `/api/DataProtection` endpoint exposes the Redis connection string. Ensure this endpoint is secured appropriately (e.g., internal network only, authentication required).

### Client Mode (Remote Configuration)

Use this mode when settings are fetched from a central configuration service (the server). Settings are fetched asynchronously using lazy initialization - the HTTP call happens on first access to the data protection service, not during startup.

> **Note:** The health check will report `Unhealthy` until initialization completes. This is by design - it prevents load balancers from sending traffic before the application is ready.

**appsettings.json:**

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

**Program.cs (Basic):**

```csharp
using Ruya.AspNetCore.DataProtection.StackExchangeRedis;

var builder = WebApplication.CreateBuilder(args);

// Add data protection client that fetches settings from remote endpoint
builder.Services.AddDataProtectionClient(defaultPurpose: "MyApplication.Encryption");

// Add distributed tracing (required dependency)
builder.Services.AddDistributedTracingService();

var app = builder.Build();

app.Run();
```

**Program.cs (With Connection String Capture):**

Use the `configureSettings` callback to capture the Redis connection string for use elsewhere in your application (e.g., for caching):

```csharp
using Ruya.AspNetCore.DataProtection.StackExchangeRedis;

var builder = WebApplication.CreateBuilder(args);

string? redisConnectionString = null;

// Add data protection client with settings callback
builder.Services.AddDataProtectionClient(
    defaultPurpose: "MyApplication.Encryption",
    configureSettings: options =>
    {
        // Capture the connection string fetched from the server
        redisConnectionString = options.ConnectionString;
    });

// Add distributed tracing (required dependency)
builder.Services.AddDistributedTracingService();

var app = builder.Build();

// redisConnectionString is now available for other services
app.Run();
```

### Using the Service

```csharp
using Ruya.AspNetCore.DataProtection.StackExchangeRedis.Contracts;

public class MyService
{
    private readonly IDataProtection _dataProtection;

    public MyService(IDataProtection dataProtection)
    {
        _dataProtection = dataProtection;
    }

    public string EncryptSensitiveData(string plainText)
    {
        // Protect with default purpose
        return _dataProtection.Protect(plainText);
    }

    public string DecryptSensitiveData(string protectedText)
    {
        return _dataProtection.Unprotect(protectedText);
    }

    public string EncryptWithCustomPurpose(string plainText, string purpose)
    {
        // Protect with specific purpose for additional isolation
        return _dataProtection.Protect(plainText, new[] { purpose });
    }
}
```

## Configuration Reference

### DataProtectionSettings

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `ApplicationName` | string | Yes | - | Application name for key isolation |
| `ConnectionStringKey` | string | Yes | - | Key name in ConnectionStrings section |
| `CacheKey` | string | Yes | - | Redis key for storing data protection keys |
| `DefaultKeyLifetime` | int | No | 90 | Key lifetime in days (1-365) |

### DataProtectionClientSettings

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `ConnectionStringKey` | string | Yes | Key name in ConnectionStrings section |
| `Endpoint` | string | Yes | API endpoint path for fetching settings |

## Health Checks

A health check named `dataprotection-redis` is **automatically registered** when you call `AddDataProtectionServer()` or `AddDataProtectionClient()`. It verifies:

1. Initialization status (client mode only)
2. Redis connectivity (ping latency)
3. Data protection roundtrip (encrypt/decrypt test)

To expose the health check endpoint, add:

```csharp
app.MapHealthChecks("/health");
```

### Health Check States

**Client Mode (during initialization):**
```json
{
  "status": "Unhealthy",
  "results": {
    "dataprotection-redis": {
      "status": "Unhealthy",
      "description": "Data protection settings are not yet initialized."
    }
  }
}
```

**Healthy (after initialization):**
```json
{
  "status": "Healthy",
  "results": {
    "dataprotection-redis": {
      "status": "Healthy",
      "description": "Redis ping: 1ms, Data protection: OK"
    }
  }
}
```

**Degraded (high latency):**
```json
{
  "status": "Degraded",
  "results": {
    "dataprotection-redis": {
      "status": "Degraded",
      "description": "Redis ping latency is high: 5500ms"
    }
  }
}
```

## Metrics

The following metrics are emitted:

| Metric | Type | Description |
|--------|------|-------------|
| `dataprotection.protect.operations` | Counter | Total protect operations |
| `dataprotection.unprotect.operations` | Counter | Total unprotect operations |
| `dataprotection.protect.failures` | Counter | Total protect failures |
| `dataprotection.unprotect.failures` | Counter | Total unprotect failures |
| `dataprotection.operation.duration` | Histogram | Operation duration in seconds |

## Dependencies

- `Ruya.Diagnostics.Abstractions` - For `IDistributedTracing`
- `Ruya.Primitives` - For `Startup.AssemblyName`/`AssemblyVersion`

Ensure you register `IDistributedTracing` in your DI container:

```csharp
builder.Services.AddDistributedTracingService();
```
