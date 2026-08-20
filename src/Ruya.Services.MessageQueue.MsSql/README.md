# Ruya.Services.MessageQueue.MsSql

SQL Server Service Broker provider for `Ruya.Services.MessageQueue`.

## Capabilities

- Durable, transactional publish through Service Broker conversations
- Transactional batch publish
- Competing consumers through `SubscribeOptions.MaxConcurrency`
- Finite, delayed retry with a stable message ID
- Dead-letter storage after explicit reject, an unhandled terminal failure, or delivery-budget exhaustion
- Transactional receive settlement: host cancellation rolls the receive back and leaves the message eligible
- Automatic producer/consumer tracing and delivery metrics from `Ruya.Services.MessageQueue`

This package implements Service Broker only. It does not contain the table-polling provider described by
older releases. Priority, delayed publish, time-to-live, consumer-group topology, and replay are not supported.
Conversation pooling is also not implemented; startup validation rejects `EnableConversationPooling = true`
instead of silently ignoring it.

## Registration

The standard host path binds `MsSqlOptions` from `MessageQueue:MsSql` and validates it during startup:

```json
{
  "MessageQueue": {
    "MsSql": {
      "MessageQueueConnectionStringKey": "ServiceBrokerMessageQueue",
      "ReceiveTimeoutMs": 1000
    }
  }
}
```

Provide the resolved value through secrets or another configuration provider under
`ConnectionStrings:ServiceBrokerMessageQueue`. Do not place credential-bearing SQL connection strings
in base settings files; a checked-in local fallback belongs only in `appsettings.Development.json`.

```csharp
services
    .AddMessageQueue()
    .AddMsSql();
```

Use the typed overload when configuration is assembled in code:

```csharp
services
    .AddMessageQueue()
    .AddMsSql(options =>
    {
        options.ConnectionString = configuration.GetConnectionString("MessageQueue")!;
        options.ReceiveTimeoutMs = 1_000;
        options.PollingIntervalMs = 100;
        options.MaxDeliveryAttempts = 5;
        options.CommandTimeoutSeconds = 30;
        options.AutoCreateSchema = true;
        options.AutoEnableServiceBroker = false;
    });
```

Enable Service Broker during database provisioning:

```sql
ALTER DATABASE [YourDatabase] SET ENABLE_BROKER;
```

`AutoEnableServiceBroker = true` can perform that operation at startup, but it requires `ALTER DATABASE`
permission and is not recommended as an application-runtime permission.

When `AutoCreateSchema` is enabled, every provider creation runs an idempotent creation/upgrade script for
the message type, contract, per-topic queue/service procedure, existing topic queues, and dead-letter table.
The application identity therefore needs the corresponding DDL permission. When production identities are
DML-only, run `Resources/SQL/ServiceBrokerSchema.sql` during deployment, pre-create every topic, and set
`AutoCreateSchema = false`.

Topic queues use `POISON_MESSAGE_HANDLING (STATUS = OFF)` because Ruya owns the finite retry/DLQ policy.
The upgrade and topic-creation procedure change that setting only when automatic poison handling is still
enabled; they never re-enable an operator-stopped queue. Without the repair, SQL Server's automatic poison
detection can disable a queue after five receive-transaction rollbacks—including repeated graceful shutdown
cancellations of the same delivery.

## Publish

```csharp
var queue = await messageQueueFactory.CreateQueueAsync("orders-mssql", cancellationToken);

var messageId = await queue.PublishAsync(
    "orders.created",
    new OrderCreated(orderId),
    new PublishOptions
    {
        MessageId = persistedOutboxId.ToString("D"),
        CorrelationId = correlationId,
        Headers = new Dictionary<string, object>
        {
            ["tenant"] = tenant,
        },
    },
    cancellationToken);
```

`PublishOptions.MessageId` is optional for an ordinary publish and required when an upstream durable Outbox
owns identity. The provider returns and serializes that exact value. Caller-assigned `MessageId` is rejected
for `PublishBatchAsync`, because one ID cannot identify several logical messages.

Batch publication commits all messages in one SQL transaction. Publish telemetry is completed only after
that transaction commits.

## Subscribe

```csharp
await using var subscription = await queue.SubscribeAsync<OrderCreated>(
    "orders.created",
    async context =>
    {
        await orderService.ProcessAsync(context.Envelope.Payload, context.CancellationToken);
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

Each worker uses `WAITFOR (RECEIVE TOP(1) ...)` inside an explicit SQL transaction:

| Handler result | Provider action |
|---|---|
| `Success` | End the conversation and commit. |
| `Retry` | Wait for the configured delay, replace the delivery transactionally with the same message ID and an incremented durable attempt count, then commit. |
| `Retry` at the cap | Move the message to the dead-letter table and report the applied outcome as `Reject`. |
| `Reject` | Move the message to the dead-letter table and commit. |
| Unhandled exception | Dead-letter by default; when `RequeueOnException` is enabled, use the same finite retry budget. |
| Host cancellation | Roll back the receive, emit no completed-delivery metric, and propagate cancellation. |

The retry delay occurs before the replacement transaction commits. This deliberately holds the delivery
transaction open so cancellation can roll it back without a loss window. Retry delays use bounded
exponential backoff and jitter by default.

MessageQueue delivery is at least once. Make the handler idempotent or use the atomic Inbox integration from
`Ruya.Services.ReliableMessaging.MessageQueue`. External side effects cannot join the Service Broker receive
transaction and need their own idempotency key or Outbox.

## Observability

The provider reports `messaging.system = mssql.service_broker`. Instrument names, enable/disable behavior,
bounded metric labels, and W3C propagation are documented in the
[`Ruya.Services.MessageQueue` README](../Ruya.Services.MessageQueue/README.md#automatic-telemetry).

Queue depth and dead-letter state remain broker/database signals; export them from SQL Server monitoring
rather than duplicating them as service-owned counters.

## Operational notes

- `ReceiveTimeoutMs` controls the blocking `WAITFOR` duration. `0` selects non-blocking `RECEIVE`.
- `PollingIntervalMs` is applied only when no message was returned.
- `BatchSize` is reserved. Transactional delivery currently receives one message per transaction;
  `SubscribeOptions.MaxConcurrency` controls parallelism.
- `ConsumerGroup`, routing patterns, and prefetch do not create separate Service Broker topology.
- Cancellation of the token passed to `SubscribeAsync` ends that subscription lifetime. It cannot be
  resumed; create a new subscription. Disposal also cancels every receive worker, waits for rollback/exit,
  and releases resources.
- Dead-letter IDs are stored as provider-neutral text and payloads as the serializer's exact bytes in
  `dbo.RuyaServicesMessageQueueDeadLetter`. The original envelope timestamp is retained when decoding
  succeeded. Upgrading the old text payload column preserves existing values as SQL Server binary data;
  only new rows are guaranteed to contain the original serializer bytes.
