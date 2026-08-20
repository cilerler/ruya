# Ruya.Services.ReliableMessaging.MessageQueue

`Ruya.Services.MessageQueue` adapter for `Ruya.Services.ReliableMessaging`.

Provides:

- **`MessageQueueOutboundDispatcher`** — `IOutboundDispatcher` implementation that forwards outbox envelopes to `IMessageQueue.PublishAsync` via `IMessageQueueFactory`.
- **`SubscribeWithInboxAsync`** extension on `IMessageQueue` — scope-aware atomic consumer processing via
  `IAtomicInboxStore<TDbContext>`.

## Producer side (Outbox → MessageQueue)

```csharp
services
    .AddMessageQueue()
    .AddJsonSerializerContext(RecipeContractsJsonSerializerContext.Default)
    .AddRabbitMQ();

services
    .AddReliableMessaging(options =>
    {
        options.Outbox.PollInterval = TimeSpan.FromSeconds(1);
    })
    .AddOutboxContext<RecipeDbContext>()
    .AddEntityFrameworkOutboxStore<RecipeDbContext>()
    .AddMessageQueueOutboundDispatcher(options =>
    {
        options.QueueName = "rabbitmq"; // matches a provider key in MessageQueue:Providers
    });
```

`AddMessageQueue()` binds `MessageQueue` and the dispatcher binds
`ReliableMessaging:MessageQueueDispatcher` directly from configuration. Both option graphs are validated when
the host starts; a missing, blank, unconfigured, or disabled fallback provider name is rejected.

When the outbox processor dispatches an envelope:

1. Resolve the explicitly named queue/provider from `envelope.DispatcherName`; use `options.QueueName` only when the envelope name is null or whitespace.
2. Deserialize `envelope.PayloadJson` to the runtime type declared by `envelope.PayloadType`. When
   producer-owned contexts were registered with `AddJsonSerializerContext`, the dispatcher uses the
   matching source-generated `JsonTypeInfo`; a missing contract fails explicitly. With no registered
   contexts, the legacy Web JSON reflection path remains available for backward compatibility.
3. Build publish options with the persisted `envelope.MessageId` plus reconstructed correlation, causation, source, and custom headers.
4. Invoke `queue.PublishAsync<TMessage>(envelope.Topic, payload, publishOptions, ct)` via cached reflected generic method.

`envelope.DispatcherName` on a per-envelope basis can target a different configured queue/provider name,
overriding the default. Provider selection does not imply a different transport endpoint: the current
RabbitMQ package gives every RabbitMQ name the same `MessageQueue:RabbitMQ` broker/vhost configuration.
Re-dispatching the same Outbox row therefore uses both the same provider selection and the same broker
message ID, allowing an Inbox-protected consumer to recognize it as the same delivery.

## Consumer side (MessageQueue → Inbox dedup)

```csharp
// In your consumer background service:
await using var subscription = await queue.SubscribeWithInboxAsync<RecipeChangedEvent, StoreDbContext>(
    topic: "recipes.changed",
    consumerName: "Store.RecipeSnapshotProjector",
    scopeFactory: _scopeFactory,
    handler: async (services, context) =>
    {
        // Resolve scoped dependencies from this provider. Business writes made
        // through this StoreDbContext enlist in the atomic Inbox transaction.
        var db = services.GetRequiredService<StoreDbContext>();
        db.RecipeSnapshots.Update(...);
        await db.SaveChangesAsync(context.CancellationToken);
        return MessageResult.Success();
    });

await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
```

Or with auto-derived consumer name (handler-type convention via `IInboxConsumerNameProvider`):

```csharp
await using var subscription = await queue.SubscribeWithInboxAsync<RecipeChangedEvent, StoreDbContext, RecipeSnapshotProjector>(
    topic: "recipes.changed",
    scopeFactory: _scopeFactory,
    consumerNameProvider: _consumerNameProvider,
    handler: async (services, context) =>
    {
        var projector = services.GetRequiredService<RecipeSnapshotProjector>();
        return await projector.ProcessAsync(context);
    });

await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
```

The `THandlerMarker` type parameter is used only for consumer-name derivation (attribute or full type name). If the class is annotated with `[InboxConsumerName("custom.name")]`, that wins.

### Transaction and result semantics

The scope-aware overload creates one asynchronous DI scope per delivery. The atomic Inbox store and handler
share that scope; business writes made through its transaction-owning `TDbContext` enlist in the Inbox
transaction. Resolving another resource from the scope does not automatically enlist that resource.

| Handler outcome | Inbox and enlisted business transaction | Broker-facing result |
|---|---|---|
| `MessageResult.Success()` | Commit business changes and a `Processed` Inbox row together | `Success` |
| `MessageResult.Retry(...)` | Roll back all enlisted changes, including the Inbox claim | Original `Retry` |
| `MessageResult.Reject(...)` | Roll back all enlisted changes, including the Inbox claim | Original `Reject` |
| Exception | Roll back all enlisted changes | Exception propagates |
| Existing committed `Processed` row | Do not invoke the handler | `Success` |

A provider execution strategy may invoke the transactional handler again after a transient database failure.
Database work in the shared transaction is safe, but HTTP calls, emails, broker publishes, and other external
side effects are not rolled back. Make those effects independently idempotent, or write an Outbox entry in the
shared transaction and perform the effect later.

### Migrating from the handler-only overload

The handler-only overload is retained for source compatibility, but it cannot give the handler the
transaction-owning scope. Migrate captured scoped dependencies to the supplied `IServiceProvider`; otherwise
the Inbox record and business state are not guaranteed to use the same transaction.

`IInboxStore<TDbContext>.TryRecordAsync` and `MarkProcessedAsync` also remain available as low-level manual
primitives. Direct callers own the transaction, result mapping, and recovery policy.

> [!WARNING]
> Reconcile existing non-processed Inbox rows before enabling the atomic path. In particular, a legacy
> `Received` row is ambiguous: the old flow may have persisted it before the handler ran, or the handler's
> business commit may have succeeded before `MarkProcessedAsync` ran. Do not blindly replay or delete the row,
> and do not treat it as a safe duplicate. Compare it with the consumer's business state, then deliberately
> mark it `Processed` only if the effect committed, or remove it for replay only if the effect did not commit.
> The atomic EF store fails closed on `Received` and `Failed` rows until that reconciliation is complete.

## Notes

- `MessageQueueOutboundDispatcher` uses cached reflection to invoke the generic `IMessageQueue.PublishAsync<TMessage>` because the concrete payload type is not known at compile time (it comes from persisted metadata). The `MethodInfo` is cached per payload type; overhead is negligible after the first call per type.
- The canonical `SubscribeWithInboxAsync` overload requires `IAtomicInboxStore<TDbContext>`; the EF Core adapter
  registers its `EntityFrameworkInboxStore<TDbContext>` implementation for that contract.
- This package does **not** install a pipeline-wide MessageQueue middleware for Inbox dedup. Dedup is subscriber-scoped via `SubscribeWithInboxAsync<TMessage, TDbContext>(...)` so each subscription picks its own `(ConsumerName, TDbContext)` pair. The rationale is captured in the design doc ([../Ruya.Services.ReliableMessaging/docs/design/reliable-messaging.md](../Ruya.Services.ReliableMessaging/docs/design/reliable-messaging.md)) under *Alternatives Considered → Pipeline-wide middleware for consumer-side Inbox dedup*.
