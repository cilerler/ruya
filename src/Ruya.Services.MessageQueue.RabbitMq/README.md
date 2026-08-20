# Ruya.Services.MessageQueue.RabbitMq

RabbitMQ provider implementation for `Ruya.Services.MessageQueue`. It leverages `RabbitMQ.Client` to provide robust, durable messaging capabilities.

## Features

-   **Durable Messaging**: Supports persistent exchanges and queues.
-   **Routing**: Topic, Direct, and Fanout exchanges.
-   **Delayed Delivery**: Supports `x-delayed-message` plugin.
-   **Priority Queues**: Supports `x-max-priority`.
-   **Reliability**: Publisher confirms and consumer acknowledgments.

## Configuration

Register the message-queue core and the RabbitMQ provider in `Program.cs`:

```csharp
services
    .AddMessageQueue()
    .AddRabbitMQ();
```

`AddRabbitMQ()` binds `RabbitMQOptions` from `MessageQueue:RabbitMQ` and validates the options when the
host starts. The raw `IConfiguration` overload remains only as an obsolete 8.x compatibility bridge. Use the
`Action<RabbitMQOptions>` overload only when composition already owns strongly typed values, such as an
isolated test fixture.

The following is a disposable local-broker example for `appsettings.Development.json` only:

```json
{
  "MessageQueue": {
    "Providers": {
      "orders-rabbitmq": {
        "Enabled": true,
        "Type": "RabbitMQ"
      }
    },
    "RabbitMQ": {
      "Host": "localhost",
      "Port": 5672,
      "VirtualHost": "/",
      "Username": "useradmin",
      "Password": "passwordadmin",
      "UseSsl": false,
      "ConnectionTimeout": "00:00:30",
      "UsePublisherConfirms": true,
      "PublisherConfirmTimeout": "00:00:05"
    }
  }
}
```

Do not copy these disposable credentials into base or deployed settings. For non-Development environments,
omit credential values from checked-in files and supply `MessageQueue__RabbitMQ__Username` and
`MessageQueue__RabbitMQ__Password` through the application's secret provider.

Topology is derived from publish topics and subscription options at runtime. `RabbitMQOptions` does not
contain configuration arrays for exchanges, queues, or bindings.

When `UsePublisherConfirms` is enabled, `PublisherConfirmTimeout` bounds confirmation waiting for one publish
or for the complete batch in `PublishBatchAsync`. `PublishOptions.WaitForConfirmation = false` explicitly uses
a non-confirming channel for that publish or batch. It cannot enable confirms when `UsePublisherConfirms` is
disabled globally. The core `PublishOptions.Timeout` remains the outer operation deadline and overrides
`MessageQueue:DefaultTimeout` for that call.

## Usage

### Publishing with Priority and Delay

```csharp
await _queue.To<OrderEvent>("orders")
    .WithPriority(10)                    // High priority
    .WithDelay(TimeSpan.FromMinutes(30)) // Delayed delivery (requires plugin)
    .SendAsync(new OrderEvent { ... });
```

### Known Limitations

-   **Single Connection Configuration**: All named RabbitMQ queue instances use the one
    `MessageQueue:RabbitMQ` broker/vhost configuration.
-   **Delayed Messages**: Requires the [RabbitMQ Delayed Message Plugin](https://github.com/rabbitmq/rabbitmq-delayed-message-exchange)
    and `DefaultExchangeType` set to `x-delayed-message`.
-   **RabbitMQ Streams**: The AMQP provider does not implement Streams. `UseStreams: true` or a non-null
    `StreamOptions` value is rejected during options validation; both properties remain only for source
    compatibility.
-   **TLS**: `UseSsl: true` enables the RabbitMQ client's TLS transport. The broker certificate must be valid
    for the configured host and trusted by the application environment.
