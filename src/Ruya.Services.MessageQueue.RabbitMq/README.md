# Ruya.Services.MessageQueue.RabbitMq

RabbitMQ provider implementation for `Ruya.Services.MessageQueue`. It leverages `RabbitMQ.Client` to provide robust, durable messaging capabilities.

## Features

-   **Durable Messaging**: Supports persistent exchanges and queues.
-   **Advanced Routing**: Topic, Direct, Fanout, and Headers exchanges.
-   **Delayed Delivery**: Supports `x-delayed-message` plugin.
-   **Priority Queues**: Supports `x-max-priority`.
-   **Reliability**: Publisher confirms and consumer acknowledgments.

## Configuration

Add the provider to your `Startup.cs` or `Program.cs`:

```csharp
services.AddMessageQueue(options =>
{
    options.DefaultProvider = "rabbitmq";
})
.AddRabbitMQ(options =>
{
    options.Host = "localhost";
    options.Port = 5672;
    options.Username = "guest";
    options.Password = "guest";
    options.VirtualHost = "/";
    
    // Optional: Enable publisher confirms for data safety
    options.UsePublisherConfirms = true;
});
```

### Advanced Configuration (appsettings.json)

You can define topology in `appsettings.json`:

```json
"RabbitMQ": {
  "Host": "localhost",
  "Exchanges": [
    {
      "Name": "orders",
      "Type": "topic",
      "Durable": true
    }
  ],
  "Queues": [
    {
      "Name": "orders.processing",
      "Durable": true,
      "Arguments": {
        "x-max-priority": 10,
        "x-dead-letter-exchange": "orders.dlx"
      }
    }
  ],
  "Bindings": [
    {
      "Source": "orders",
      "Destination": "orders.processing",
      "RoutingKey": "order.*"
    }
  ]
}
```

## Usage

### Publishing with Priority and Delay

```csharp
await _queue.To<OrderEvent>("orders")
    .WithPriority(10)                    // High priority
    .WithDelay(TimeSpan.FromMinutes(30)) // Delayed delivery (requires plugin)
    .SendAsync(new OrderEvent { ... });
```

### Known Limitations

-   **Single Host**: Currently connects to a single RabbitMQ broker/vhost per provider instance.
-   **Delayed Messages**: Requires the [RabbitMQ Delayed Message Plugin](https://github.com/rabbitmq/rabbitmq-delayed-message-exchange) installed on the broker.
