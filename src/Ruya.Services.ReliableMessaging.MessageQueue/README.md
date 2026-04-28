# Ruya.Services.ReliableMessaging.MessageQueue

`Ruya.Services.MessageQueue` adapter for `Ruya.Services.ReliableMessaging`.

Provides:

- **`MessageQueueOutboundDispatcher`** — `IOutboundDispatcher` implementation that forwards outbox envelopes to `IMessageQueue.PublishAsync` via `IMessageQueueFactory`.
- **`SubscribeWithInboxAsync`** extension on `IMessageQueue` — consumer-side dedup via `IInboxStore<TDbContext>.TryRecordAsync` before the handler runs.

## Producer side (Outbox → MessageQueue)

```csharp
services
    .AddMessageQueue(configuration)
    .AddRabbitMQ(...)
    .AddMsSql(...);

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

When the outbox processor dispatches an envelope:

1. Resolve queue via `IMessageQueueFactory.CreateQueueAsync(envelope.DispatcherName ?? options.QueueName)`.
2. Deserialize `envelope.PayloadJson` to the runtime type declared by `envelope.PayloadType`.
3. Invoke `queue.PublishAsync<TMessage>(envelope.Topic, payload, publishOptions, ct)` via cached reflected generic method.

`envelope.DispatcherName` on a per-envelope basis can target a different queue/provider (e.g. a second RabbitMQ cluster), overriding the default.

## Consumer side (MessageQueue → Inbox dedup)

```csharp
// In your consumer background service:
await queue.SubscribeWithInboxAsync<RecipeChangedEvent, StoreDbContext>(
    topic: "recipes.changed",
    consumerName: "Store.RecipeSnapshotProjector",
    scopeFactory: _scopeFactory,
    handler: async context =>
    {
        // Handler runs only on first receipt. Duplicates short-circuit to MessageResult.Success().
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        db.RecipeSnapshots.Update(...);
        await db.SaveChangesAsync(context.CancellationToken);
        return MessageResult.Success();
    });
```

Or with auto-derived consumer name (handler-type convention via `IInboxConsumerNameProvider`):

```csharp
await queue.SubscribeWithInboxAsync<RecipeChangedEvent, StoreDbContext, RecipeSnapshotProjector>(
    topic: "recipes.changed",
    scopeFactory: _scopeFactory,
    consumerNameProvider: _consumerNameProvider,
    handler: async context => { /* ... */ return MessageResult.Success(); });
```

The `THandlerMarker` type parameter is used only for consumer-name derivation (attribute or full type name). If the class is annotated with `[InboxConsumerName("custom.name")]`, that wins.

## Notes

- `MessageQueueOutboundDispatcher` uses cached reflection to invoke the generic `IMessageQueue.PublishAsync<TMessage>` because the concrete payload type is not known at compile time (it comes from persisted metadata). The `MethodInfo` is cached per payload type; overhead is negligible after the first call per type.
- `SubscribeWithInboxAsync` creates a DI scope per message to resolve `IInboxStore<TDbContext>`. If your pipeline already runs handlers inside a scope, consider wiring the inbox check directly against the existing scope for efficiency.
- This package does **not** install a pipeline-wide MessageQueue middleware for Inbox dedup. Dedup is subscriber-scoped via `SubscribeWithInboxAsync<TMessage, TDbContext>(...)` so each subscription picks its own `(ConsumerName, TDbContext)` pair. The rationale is captured in the design doc ([../Ruya.Services.ReliableMessaging/docs/design/reliable-messaging.md](../Ruya.Services.ReliableMessaging/docs/design/reliable-messaging.md)) under *Alternatives Considered → Pipeline-wide middleware for consumer-side Inbox dedup*.
