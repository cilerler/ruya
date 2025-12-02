# Ruya.Services.MessageQueue.Redis

Redis provider implementation for `Ruya.Services.MessageQueue`. Uses Redis Pub/Sub and Streams for high-performance, low-latency messaging.

## Features

-   **High Performance**: Extremely low latency for real-time scenarios.
-   **Pub/Sub**: Standard Redis Publish/Subscribe.
-   **Streams**: Durable messaging via Redis Streams (Consumer Groups).
-   **Simple Setup**: Minimal configuration required.

## Configuration

```csharp
services.AddMessageQueue(options =>
{
    options.DefaultProvider = "redis";
})
.AddRedis(options =>
{
    options.ConnectionString = "localhost:6379,abortConnect=false";
    options.Database = 0;
    
    // Choose mode: PubSub (fire-and-forget) or Streams (durable)
    options.UseStreams = true; 
    options.ConsumerGroup = "my-service-group";
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

### Durable Processing (Streams)

Use Redis Streams for reliable processing with consumer groups.

```csharp
// Configure with UseStreams = true
await _queue.SubscribeAsync<OrderEvent>("orders", async context =>
{
    // Process order
    return MessageResult.Success(); // Acknowledges the stream entry
});
```
