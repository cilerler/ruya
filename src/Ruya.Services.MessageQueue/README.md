# Ruya.Services.MessageQueue

Provider-neutral asynchronous publishing and subscription APIs for .NET. The core package owns
serialization, named-provider creation, custom middleware composition, W3C trace propagation, and
delivery telemetry. Transport packages own broker topology and settlement.

## Packages

| Package | Provider type | Delivery model |
|---|---|---|
| `Ruya.Services.MessageQueue.InMemory` | `InMemory` | Process-local topics and consumer groups; useful for tests and single-process work. |
| `Ruya.Services.MessageQueue.RabbitMq` | `RabbitMQ` | Durable queues, competing consumers, publisher confirms, retry/reject, and DLQ topology. |
| `Ruya.Services.MessageQueue.Redis` | `Redis` | Pub/Sub subscriptions with no ack/redelivery; Streams are currently publish-only. |
| `Ruya.Services.MessageQueue.MsSql` | `MsSql` | SQL Server Service Broker with transactional publish, receive settlement, retry, and dead-letter storage. |

Provider-specific READMEs are authoritative for capabilities and operational constraints. Do not assume a
`MessageResult` has the same broker effect on transports that lack acknowledgment or redelivery.

## Registration

Bind named provider instances in `MessageQueue:Providers`, then register the corresponding provider types.
The provider-specific README owns its transport configuration and secret-handling rules:

```json
{
  "MessageQueue": {
    "EnableTelemetry": true,
    "EnableHealthChecks": true,
    "Serializer": "json",
    "DefaultTimeout": "00:00:30",
    "Providers": {
      "orders-rabbitmq": {
        "Enabled": true,
        "Type": "RabbitMQ"
      }
    }
  }
}
```

Do not place a connection string under a named `MessageQueue:Providers` entry. The released
`ProviderConfiguration.ConnectionString` property was never consumed and now rejects nonblank values at
startup. Configure the selected transport's descriptive `*ConnectionStringKey` in its provider section and
supply the matching `ConnectionStrings` value through secrets or another configuration provider.

```csharp
services
    .AddMessageQueue()
    .AddJsonSerializerContext(OrderContractsJsonSerializerContext.Default)
    .AddRabbitMQ();
```

`AddMessageQueue()` binds `MessageQueueOptions` from `MessageQueue` and validates the result when the host
starts. `AddRabbitMQ()` binds and validates `MessageQueue:RabbitMQ`. Application composition should use these
parameterless APIs; the raw `IConfiguration` overloads remain only as obsolete 8.x compatibility bridges.
An `Action<MessageQueueOptions>` overload is available for tests and compositions that already own strongly
typed values.

The named `Serializer` setting currently accepts only `json`; unsupported names fail during startup. Register
a custom serializer explicitly with `AddSerializer<TSerializer>()`. When `EnableHealthChecks` is `false`,
registered message-queue health checks report that they are disabled without creating or querying queues.

`AddSingletonMessageQueue(name)` remains as an obsolete 8.x bridge. It now returns an async-lazy proxy and
never blocks service resolution; new code should inject `IMessageQueueFactory` and await
`CreateQueueAsync(name)` explicitly.

See the
[`Ruya.Services.MessageQueue.RabbitMq` README](../Ruya.Services.MessageQueue.RabbitMq/README.md) for the
Development-only local example and the non-Development secrets contract.

Register every producer-owned contract context used by this process. The default JSON serializer
reflection-resolves only Ruya's generic envelope and framework metadata; the envelope payload is read and
written with the producer's `JsonTypeInfo`. Once any context is registered, a payload missing from all
registered contexts fails explicitly instead of silently falling back to reflection. Custom serializers own
an equivalent explicit metadata contract. Contexts are queried in registration order; register overlapping
contract metadata once.

Other registrations use `.AddInMemoryProvider(...)`, `.AddRedis(...)`, or `.AddMsSql(...)`.

`IMessageQueueFactory.CreateQueueAsync(name, cancellationToken)` requires the configured instance name
(`orders-rabbitmq` above). It caches and owns that queue; callers do not dispose the shared instance.
`DefaultProvider` is retained configuration metadata but does not replace explicit instance selection.

```csharp
var queue = await messageQueueFactory.CreateQueueAsync(
    settings.MessageQueueProviderName,
    cancellationToken);
```

`MessageQueue:DefaultTimeout` bounds finite queue operations such as queue creation, publishing, and health
checks. `PublishOptions.Timeout` overrides that deadline for one publish or batch. Caller-requested
cancellation remains cancellation; expiration of a configured deadline raises `TimeoutException`.
Subscription tokens are forwarded unchanged because providers may retain them for the complete subscription
lifetime; cancelling the host token after setup therefore continues to reach handlers.

## Publishing

```csharp
var messageId = await queue.PublishAsync(
    settings.OrderCreatedEventTopicName,
    new OrderCreatedEvent(orderId),
    new PublishOptions
    {
        CorrelationId = correlationId,
        Source = "orders",
    },
    cancellationToken);
```

Providers generate `MessageId` when it is omitted. When a durable Outbox owns identity, pass that persisted
identifier on every dispatch attempt:

```csharp
new PublishOptions
{
    MessageId = persistedEnvelopeId.ToString("D"),
    CorrelationId = correlationId,
    CausationId = causationId,
    Source = source,
    Headers = headers,
    Timeout = TimeSpan.FromSeconds(10),
}
```

InMemory, RabbitMQ, Redis, and SQL Server preserve and return the caller-supplied value. Every provider
rejects a caller-assigned `MessageId` for `PublishBatchAsync`, because a batch contains multiple logical
messages and therefore needs one generated ID per message.

## Subscribing

A long-lived host component owns and asynchronously disposes the subscription handle:

```csharp
await using var subscription = await queue.SubscribeAsync<OrderCreatedEvent>(
    settings.OrderCreatedEventTopicName,
    async context =>
    {
        await orderService.ProcessAsync(
            context.Envelope.Payload,
            context.CancellationToken);
        return MessageResult.Success();
    },
    new SubscribeOptions
    {
        MaxConcurrency = 4,
        MaxDeliveryCount = 3,
        RetryPolicy = new RetryPolicy
        {
            MaxRetryAttempts = 2,
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(4),
            BackoffMultiplier = 2,
            UseExponentialBackoff = true,
            UseJitter = true,
        },
    },
    stoppingToken);
```

- `Success` requests acknowledgment.
- `Retry` requests redelivery under the selected provider's finite retry capability.
- `Reject` requests terminal rejection or dead-lettering.
- An unknown exception remains `unhandled`; `RequeueOnException` controls whether capable providers retry it.
- Host-requested cancellation propagates. Capable providers leave the delivery unsettled or roll it back;
  cancellation is not converted to poison or counted as a completed delivery.

Set an explicit finite `MaxDeliveryCount` and nonzero `RetryPolicy` whenever the handler returns `Retry`.
Read the selected provider README first: Redis Pub/Sub, for example, cannot honor retry or reject.

Delivery is at least once unless a provider contract says otherwise. Keep handlers idempotent. For an atomic
database Inbox, use the scope-aware and post-commit APIs in
[`Ruya.Services.ReliableMessaging.MessageQueue`](../Ruya.Services.ReliableMessaging.MessageQueue/README.md).
They commit the inbox claim and business mutation together and keep committed-state telemetry outside the
retryable transaction callback.

## Automatic telemetry

`AddMessageQueue` registers its OpenTelemetry source and meter once. Applications configure exporters; they
do not repeat the implementation-owned source/meter names.

| Signal | Contract |
|---|---|
| ActivitySource | `Ruya.Services.MessageQueue` |
| Meter | `Ruya.Services.MessageQueue` |
| Producer span | `publish {destination}`, `ActivityKind.Producer` |
| Consumer span | `consume {destination}`, `ActivityKind.Consumer` |
| Delivery counter | `ruya.message_queue.delivery.attempts`, unit `{attempt}` |
| Delivery histogram | `ruya.message_queue.delivery.duration`, unit `s` |

The counter and histogram carry only bounded delivery labels:

- `messaging.system`
- `messaging.destination.name`
- optional `messaging.consumer.group.name`
- `ruya.message_queue.outcome` with exactly `success`, `retry`, `reject`, or `unhandled`

Provider values are `in_memory`, `rabbitmq`, `redis`, and `mssql.service_broker`. Message IDs, message types,
exception text, reasons, generated consumer IDs, and delivery counts are span/log detail—not metric labels.

The provider starts a delivery boundary before deserialization and completes it only after broker-facing
settlement. Malformed input, middleware failure, handler failure, and settlement failure therefore report one
`unhandled` outcome. A handler-requested retry that reaches its cap reports the applied `reject` outcome.
Host cancellation reports neither an outcome nor duration.

When `MessageQueue:EnableTelemetry` is `true` (the default), publishing injects `traceparent` and `tracestate`
into a copied envelope, and delivery extracts them before the consumer span. When it is `false`, queue spans,
queue delivery metrics, and queue propagation are disabled. Logs and health behavior are unaffected.

`TelemetryMiddleware` is a compatibility no-op and explicit registration is ignored. Automatic provider
instrumentation is the single queue telemetry layer. Do not also collect a broker client's producer/consumer
ActivitySources for the same operation unless duplicate spans are intentionally accepted.

Environment and service identity are OpenTelemetry resource attributes. Whether a metrics backend exposes
them as Prometheus labels is an exporter/collector resource-to-telemetry decision; the queue does not add
unbounded or duplicated resource tags to every instrument.

## Custom middleware

Custom `IMessageMiddleware` remains supported for application concerns such as validation or policy:

```csharp
services
    .AddMessageQueue()
    .AddMiddleware<ValidationMessageMiddleware>();
```

Middleware runs inside the provider boundary. It does not own transport settlement, transport spans, or the
delivery outcome instruments.

## Capability summary

| Capability | InMemory | RabbitMQ | Redis | SQL Server Service Broker |
|---|---:|---:|---:|---:|
| Batch publish | Yes | Yes | Yes | Yes |
| Caller-assigned single-message ID | Yes | Yes | Yes | Yes |
| Consumer groups / competing consumers | Yes | Yes | No (Pub/Sub broadcasts) | Competing consumers only |
| Retry / redelivery | Process-local | Yes | No | Yes |
| Dead-letter handling | Process-local | Yes | No | SQL table |
| Durable transactions | No | No | No | Yes |
| Replay | No | No | No | No |

This table describes the current implementations, not what their underlying technologies could support with
additional code or plugins. Provider `Capabilities` must stay consistent with these implemented paths.
