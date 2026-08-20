# Distributed Tracing

## Registration

Register metrics and an `IDistributedCache` implementation first, then add the tracing service:

```csharp
builder.Services.AddMetrics();
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis"));
builder.Services.AddDistributedTracingService();
```

```json
{
  "DistributedTracing": {
    "CacheSlidingExpiration": "00:30:00",
    "CacheAbsoluteExpiration": "02:00:00",
    "EnableDebugLogging": false,
    "DefaultTags": {
      "app.component": "worker"
    }
  }
}
```

The absolute expiration, when present, must not be shorter than the sliding expiration. Default tags are applied to every created activity; operation-specific tags override the same key.

## Trace continuation

Use `StartActivityAsync` for the initiator that stores a trace context and `ContinueActivityAsync` for followers that only read it. These methods use the distributed cache asynchronously and accept cancellation. Cache failures are reported with stable event IDs but do not fail the business operation. Cache keys are not written to logs or metric labels. The synchronous methods remain for compatibility and for operations that do not cross a remote-cache boundary.

```csharp
using var scope = await tracing.StartActivityAsync(
    "DispatchOrder",
    ActivityKind.Producer,
    cacheKey: $"trace:order:{orderId}",
    cancellationToken: cancellationToken);
```

`ActivityScope` copies share one lifecycle state, so disposing more than one copy stops the activity only once.

Do not place personal data, credentials, message IDs, or other unbounded values in `DefaultTags` or metric labels.
