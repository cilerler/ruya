# Ruya.Services.MessageQueue.Redis

Redis provider implementation for `Ruya.Services.MessageQueue`. It supports publishing through Redis Pub/Sub or Streams; subscription currently uses Pub/Sub only.

## Features

-   **High Performance**: Extremely low latency for real-time scenarios.
-   **Pub/Sub**: Standard Redis Publish/Subscribe.
-   **Streams publishing**: Append messages to a Redis Stream. Stream consumption and consumer groups are not implemented yet.
-   **Simple Setup**: Minimal configuration required.

## Configuration

The standard configuration path binds `RedisOptions` from `MessageQueue:Redis` and validates it when the host starts:

```json
{
  "MessageQueue": {
    "Redis": {
      "RedisConnectionStringKey": "RedisMessageQueue",
      "UsePubSub": true,
      "UseStreams": false
    }
  }
}
```

Provide the resolved value through secrets or another configuration provider under
`ConnectionStrings:RedisMessageQueue`. Do not place credential-bearing Redis connection strings in
base settings files; a checked-in local fallback belongs only in `appsettings.Development.json`.

```csharp
services.AddMessageQueue(options => options.DefaultProvider = "redis")
    .AddRedis();
```

Use the typed overload when configuration is assembled in code:

```csharp
services.AddMessageQueue(options =>
{
    options.DefaultProvider = "redis";
})
.AddRedis(options =>
{
    options.ConnectionString = "localhost:6379,abortConnect=false";
    options.Database = 0;
    
    // Choose exactly one publish mode. Subscription requires Pub/Sub.
    options.UsePubSub = true;
    options.UseStreams = false;
});
```

## Usage

### Real-time Updates (Pub/Sub)

Perfect for UI notifications, cache invalidation, or chat applications.

```csharp
// Publisher
await _queue.PublishAsync("notifications", new UserNotification { Message = "Hello!" });

// Subscriber
await _queue.SubscribeAsync<UserNotification>("notifications", async context =>
{
    // Handle notification
    return MessageResult.Success();
});
```

`RoutingPattern` and every entry in `RoutingPatterns` use the shared `*`/`#` routing syntax. Redis
keeps publishes without an explicit routing key on the released literal topic channel. Explicit routing
keys use a collision-resistant, topic-scoped channel namespace, and the shared matcher is applied only
inside that logical topic. A matching pattern on another topic cannot observe or deserialize the message.

### Stream Publishing

Publishing can append to a Redis Stream, but this provider cannot subscribe to that stream yet.

```csharp
services.AddMessageQueue(options => options.DefaultProvider = "redis")
    .AddRedis(options =>
    {
        options.ConnectionString = "localhost:6379,abortConnect=false";
        options.UsePubSub = false;
        options.UseStreams = true;
    });

await _queue.PublishAsync("orders", new OrderEvent
{
    // ...
});
```

`RetryOnFailure = false` disables connection retries; otherwise `RetryCount` controls the Redis connection retry count. `IsHealthyAsync` actively connects and pings the configured database, so it reports provider state even before the first publish.

Calling `SubscribeAsync` in Streams-only mode throws `NotSupportedException`. Pub/Sub has no acknowledgements, redelivery, consumer groups, replay, or DLQ: `Retry` and `Reject` results are observable outcomes but cannot alter delivery, and cancellation cannot return an already delivered Pub/Sub message to Redis. Use RabbitMQ or another durable provider when those guarantees are required.
