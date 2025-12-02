# Ruya.Services.MessageQueue

A robust, provider-agnostic asynchronous messaging abstraction for .NET, designed with the **Provider Pattern**. It supports multiple message brokers (RabbitMQ, Redis, SQL Server, In-Memory) with a unified API, built-in observability, and middleware support.

## Design Principles

1.  **Provider Agnostic**: Core abstractions are independent of any specific broker.
2.  **Microsoft Patterns**: Follows established .NET patterns (ILogger, Options, DI).
3.  **Async First**: Full async/await support throughout.
4.  **Extensibility**: Middleware pipeline for custom behavior.
5.  **Observability**: Built-in telemetry and health checks.
6.  **Type Safety**: Strong typing with generics.

## Configuration

### 1. Registration

Configure the queue and providers in `Startup.cs` or `Program.cs`.

```csharp
services.AddMessageQueue(options =>
{
    options.DefaultProvider = "rabbitmq";
})
.AddRabbitMQ(options => 
{
    options.Host = "localhost";
    options.Port = 5672;
})
.AddRedis(options =>
{
    options.ConnectionString = "localhost:6379";
});
```

### 2. Options Pattern

Configuration uses the standard Options pattern with validation:

```json
{
  "MessageQueue": {
    "DefaultProvider": "rabbitmq",
    "Providers": {
      "rabbitmq": { "Enabled": true, "Type": "RabbitMQ" }
    },
    "RabbitMQ": {
      "Host": "localhost",
      "Port": 5672
    }
  }
}
```

## Usage

### Publishing Messages

Inject `IMessageQueue` (or `IMessageQueueFactory` for named instances) and publish messages.

```csharp
public class OrderService
{
    private readonly IMessageQueue _queue;

    public OrderService(IMessageQueueFactory factory)
    {
        _queue = factory.CreateQueue("rabbitmq");
    }

    public async Task CreateOrderAsync(Order order)
    {
        // Simple publish
        await _queue.PublishAsync("orders.created", new OrderCreatedEvent 
        { 
            OrderId = order.Id 
        });

        // Fluent API with options
        await _queue.To<OrderCreatedEvent>("orders.created")
            .WithPriority(10)
            .WithDelay(TimeSpan.FromMinutes(5))
            .WithCorrelationId(Guid.NewGuid().ToString())
            .SendAsync(new OrderCreatedEvent { OrderId = order.Id });
    }
}
```

### Subscribing to Messages

Create a background service to handle incoming messages.

```csharp
public class OrderHandler : BackgroundService
{
    private readonly IMessageQueue _queue;

    public OrderHandler(IMessageQueueFactory factory)
    {
        _queue = factory.CreateQueue("rabbitmq");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _queue.SubscribeAsync<OrderCreatedEvent>(
            "orders.created",
            async context =>
            {
                var orderId = context.Envelope.Payload.OrderId;
                Console.WriteLine($"Processing order: {orderId}");
                return MessageResult.Success();
            },
            new SubscribeOptions
            {
                PrefetchCount = 10,
                MaxConcurrency = 5,
                RetryPolicy = new RetryPolicy { MaxRetryAttempts = 3 }
            },
            stoppingToken);
    }
}
```

## Patterns & Best Practices

### Idempotency Pattern

To handle duplicate processing (e.g., after a broker crash), implement idempotency:

```csharp
await queue.SubscribeAsync<OrderCreatedEvent>("orders.created", async context =>
{
    var messageId = context.Envelope.MessageId;
    
    // Check if already processed
    if (await processedMessageStore.ContainsAsync(messageId))
    {
        return MessageResult.Success(); // Skip duplicate
    }
    
    // Process the message
    await ProcessOrderAsync(context.Envelope.Payload);
    
    // Mark as processed (atomically with business logic if possible)
    await processedMessageStore.AddAsync(messageId);
    
    return MessageResult.Success();
});
```

## Advanced Concepts

### Concurrency & Thread Safety

The framework handles concurrency at multiple levels:

1.  **MaxConcurrency**: Limits how many messages are processed simultaneously per subscription.
    ```csharp
    new SubscribeOptions { MaxConcurrency = 5 } // Only 5 handlers run at once
# Ruya.Services.MessageQueue

A robust, provider-agnostic asynchronous messaging abstraction for .NET, designed with the **Provider Pattern**. It supports multiple message brokers (RabbitMQ, Redis, SQL Server, In-Memory) with a unified API, built-in observability, and middleware support.

## Design Principles

1.  **Provider Agnostic**: Core abstractions are independent of any specific broker.
2.  **Microsoft Patterns**: Follows established .NET patterns (ILogger, Options, DI).
3.  **Async First**: Full async/await support throughout.
4.  **Extensibility**: Middleware pipeline for custom behavior.
5.  **Observability**: Built-in telemetry and health checks.
6.  **Type Safety**: Strong typing with generics.

## Configuration

### 1. Registration

Configure the bus and providers in `Startup.cs` or `Program.cs`.

```csharp
services.AddMessageQueue(options =>
{
    options.DefaultProvider = "rabbitmq";
})
.AddRabbitMQ(options => 
{
    options.Host = "localhost";
    options.Port = 5672;
})
.AddRedis(options =>
{
    options.ConnectionString = "localhost:6379";
});
```

### 2. Options Pattern

Configuration uses the standard Options pattern with validation:

```json
{
  "MessageQueue": {
    "DefaultProvider": "rabbitmq",
    "Providers": {
      "rabbitmq": { "Enabled": true, "Type": "RabbitMQ" }
    },
    "RabbitMQ": {
      "Host": "localhost",
      "Port": 5672
    }
  }
}
```

## Usage

### Publishing Messages

Inject `IMessageQueue` (or `IMessageQueueFactory` for named instances) and publish messages.

```csharp
public class OrderService
{
    private readonly IMessageQueue _queue;

    public OrderService(IMessageQueueFactory factory)
    {
        _queue = factory.CreateQueue("rabbitmq");
    }

    public async Task CreateOrderAsync(Order order)
    {
        // Simple publish
        await _queue.PublishAsync("orders.created", new OrderCreatedEvent 
        { 
            OrderId = order.Id 
        });

        // Fluent API with options
        await _queue.To<OrderCreatedEvent>("orders.created")
            .WithPriority(10)
            .WithDelay(TimeSpan.FromMinutes(5))
            .WithCorrelationId(Guid.NewGuid().ToString())
            .SendAsync(new OrderCreatedEvent { OrderId = order.Id });
    }
}
```

### Subscribing to Messages

Create a background service to handle incoming messages.

```csharp
public class OrderHandler : BackgroundService
{
    private readonly IMessageQueue _queue;

    public OrderHandler(IMessageQueueFactory factory)
    {
        _queue = factory.CreateQueue("rabbitmq");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _queue.SubscribeAsync<OrderCreatedEvent>(
            "orders.created",
            async context =>
            {
                var orderId = context.Envelope.Payload.OrderId;
                Console.WriteLine($"Processing order: {orderId}");
                return MessageResult.Success();
            },
            new SubscribeOptions
            {
                PrefetchCount = 10,
                MaxConcurrency = 5,
                RetryPolicy = new RetryPolicy { MaxRetryAttempts = 3 }
            },
            stoppingToken);
    }
}
```

## Patterns & Best Practices

### Idempotency Pattern

To handle duplicate processing (e.g., after a broker crash), implement idempotency:

```csharp
await queue.SubscribeAsync<OrderCreatedEvent>("orders.created", async context =>
{
    var messageId = context.Envelope.MessageId;
    
    // Check if already processed
    if (await processedMessageStore.ContainsAsync(messageId))
    {
        return MessageResult.Success(); // Skip duplicate
    }
    
    // Process the message
    await ProcessOrderAsync(context.Envelope.Payload);
    
    // Mark as processed (atomically with business logic if possible)
    await processedMessageStore.AddAsync(messageId);
    
    return MessageResult.Success();
});
```

## Advanced Concepts

### Concurrency & Thread Safety

The framework handles concurrency at multiple levels:

1.  **MaxConcurrency**: Limits how many messages are processed simultaneously per subscription.
    ```csharp
    new SubscribeOptions { MaxConcurrency = 5 } // Only 5 handlers run at once
    ```

2.  **Channel Pooling (RabbitMQ)**: RabbitMQ channels are **not thread-safe**. Ruya.Services.MessageQueue manages a thread-safe `ChannelPool` for publishers, ensuring that concurrent publish operations never share a channel, preventing race conditions and data corruption.

### Ruya.Services.MessageQueue vs MediatR

Ruya.Services.MessageQueue and MediatR solve different problems and complement each other:

| Aspect | Ruya.Services.MessageQueue | MediatR |
|--------|----------------------------|---------|
| **Purpose** | Integration events, cross-service communication, durable messaging | In-process command/query dispatch, domain events |
| **Scope** | Distributed systems, microservices | Monolithic applications, within a single service boundary |
| **Transport** | Message brokers (RabbitMQ, Redis, ASB, etc.) | In-memory, direct method calls |
| **Durability** | Persistent messages, guaranteed delivery (broker-dependent) | Transient, fire-and-forget within process |
| **Concurrency** | Managed by broker/provider, consumer groups | Handled by application's threading model |
| **Observability** | Distributed tracing, metrics, health checks | Standard application logging, profiling |
| **Error Handling** | DLQ, retries, broker-level guarantees | Exception handling within application code |
| **Use Case** | Event-driven architectures, background tasks, inter-service communication | CQRS, domain event dispatch, internal messaging patterns |

**Recommendation**: Use MediatR for internal command handling and Ruya.Services.MessageQueue for integration events between services.

## Provider Capabilities

| Feature | RabbitMQ | Redis Pub/Sub | Redis Streams | Azure Service Bus |
|---------|----------|---------------|---------------|-------------------|
| Priority | ✅ Native | ❌ Not supported | ❌ Not supported | ✅ Native |
| Delayed Delivery | ✅ Via plugin | ⚠️ Emulated | ❌ Not supported | ✅ Native |
| Publisher Confirms | ✅ Native | ❌ Fire-and-forget | ❌ Not applicable | ✅ Native |
| Consumer Groups | ✅ Competing | ❌ Broadcast | ✅ Native | ✅ Native |
| Dead Letter Queue | ✅ Native | ⚠️ Emulated | ⚠️ Emulated | ✅ Native |

## Testing

### Unit Testing with Moq

```csharp
var mockQueue = new Mock<IMessageQueue>();
mockQueue.Setup(b => b.PublishAsync(It.IsAny<string>(), It.IsAny<MyEvent>(), ...))
       .ReturnsAsync("msg-id");
```

### Integration Testing with Testcontainers

```csharp
public class RabbitMQIntegrationTests : IAsyncLifetime
{
    private RabbitMqContainer _container;

    public async Task InitializeAsync()
    {
        _container = new RabbitMqBuilder().WithImage("rabbitmq:4-management").Build();
        await _container.StartAsync();
        
        // Configure bus to use _container.Hostname and _container.GetMappedPublicPort(5672)
    }
}
```

## Architecture

### Message Flow

#### Publishing
`Application` -> `IMessageQueue.PublishAsync` -> `Middleware Pipeline` -> `Serializer` -> `Provider` -> `Transport` -> `Broker`

#### Subscribing
`Broker` -> `Transport` -> `Provider` -> `Serializer` -> `Middleware Pipeline` -> `Application Handler` -> `MessageResult`

### Middleware Pipeline

The middleware pipeline uses the **Chain of Responsibility** pattern.

```csharp
public class ValidationMiddleware : MessageMiddleware
{
    public override async Task<string> PublishAsync<T>(MessageEnvelope<T> envelope, string topic, Func<MessageEnvelope<T>, string, Task<string>> next, CancellationToken ct)
    {
        if (envelope.Payload == null) throw new ValidationException("Payload cannot be null");
        return await next(envelope, topic);
    }
}
```

### Error Handling Strategy

-   **Transient Errors**: Retry with exponential backoff (via `RetryPolicy`).
-   **Permanent Errors**: Send to Dead Letter Queue (via `MessageResult.Reject()`).
-   **Broker Failures**: Auto-recovery and health checks.

### Observability

-   **Distributed Tracing**: OpenTelemetry integration with automatic span creation and context propagation.
-   **Metrics**: Publish/Consume duration, message counts, error rates.
-   **Health Checks**: Broker connectivity monitoring.
