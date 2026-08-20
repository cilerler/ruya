# Ruya.Services.ReliableMessaging

Transactional Outbox + Inbox primitives for reliable messaging across services.

- **Persistence-agnostic.** Implement `IOutboxStore<TContext>` / `IInboxStore<TContext>` against any store; an Entity Framework Core adapter ships as a companion package (`Ruya.Services.ReliableMessaging.EntityFrameworkCore`).
- **Transport-agnostic.** Implement `IOutboundDispatcher` against any destination; a Ruya.Services.MessageQueue adapter ships as a companion package (`Ruya.Services.ReliableMessaging.MessageQueue`).
- **Per-context.** Per-`DbContext` (or any marker `TContext`) registration isolates each module's Outbox/Inbox from others.

## Design

See the design doc at [docs/design/reliable-messaging.md](docs/design/reliable-messaging.md) for the full rationale, components, alternatives, and operations pointers. Operational documents live alongside it under `docs/runbooks/` and `docs/sops/`.

## Quick registration shape

```csharp
services
    .AddMessageQueue()
    .AddJsonSerializerContext(RecipeContractsJsonSerializerContext.Default)
    .AddRabbitMQ();

services
    .AddReliableMessaging(options =>
    {
        options.Outbox.PollInterval = TimeSpan.FromSeconds(1);
        options.Outbox.BatchSize    = 100;
        options.Outbox.MaxAttempts  = 10;
        options.Inbox.ArchiveAfter  = TimeSpan.FromDays(7);
    })
    .AddOutboxContext<RecipeDbContext>()
    .AddInboxContext<RecipeDbContext>()
    .AddEntityFrameworkOutboxStore<RecipeDbContext>()
    .AddEntityFrameworkInboxStore<RecipeDbContext>()
    .AddMessageQueueOutboundDispatcher();
```

`AddMessageQueue()` binds the provider catalog from `MessageQueue`, while the dispatcher binds its required
fallback provider name from `ReliableMessaging:MessageQueueDispatcher:QueueName`. That name must identify an
enabled entry in `MessageQueue:Providers`.

## Caller surface

```csharp
public sealed class RecipeService(
    RecipeDbContext db,
    IOutboxPublisher<RecipeDbContext> outbox,
    IOptions<RecipeSettings> options) : IRecipeService
{
    public async Task CreateAsync(RecipeCreateCommand cmd, CancellationToken ct)
    {
        var recipe = Recipe.CreateFrom(cmd);
        db.Recipes.Add(recipe);

        var message = new RecipeCreatedEvent(recipe.Id);
        await outbox.EnqueueSourceGeneratedAsync(
            options.Value.RecipeCreatedEventTopicName,
            message,
            RecipeContractsJsonSerializerContext.Default.RecipeCreatedEvent,
            new OutboxPublishOverrides
            {
                DispatcherName = options.Value.MessageQueueProviderName,
            },
            cancellationToken: ct);

        await db.SaveChangesAsync(ct);   // atomic: business row + outbox row
    }
}
```

Invalid poll, batch, durable-retry, cleanup, or fallback-dispatcher settings fail during host startup rather
than surfacing after a hosted processor begins work.

`EnqueueSourceGeneratedAsync` is the canonical application path: the producer supplies its generated
`JsonTypeInfo<TPayload>` and the Outbox stores that exact JSON contract. `EnqueueAsync` remains as a
reflection-based compatibility API for existing callers. A custom `IOutboxPublisher<TContext>` that has not
implemented the source-generated member fails explicitly rather than silently changing the wire contract.

The `SaveChangesInterceptor` provided by the storage adapter drains the outbox buffer inside `SaveChangesAsync`,
so the outbox row and business state commit in the same transaction. Rollback drops both.
When `OutboxProcessor` reconstructs a persisted envelope, it restores the original message ID, dispatcher name,
and JSON headers before calling the transport adapter. A durable retry therefore does not invent a new delivery
identity or lose correlation metadata.

Consumer-side processing is coordinated by the transport adapter's scope-aware
`SubscribeWithInboxAsync` overload. It creates one DI scope for the atomic Inbox store and the handler;
the handler must resolve its `DbContext` and other scoped business services from the supplied
`IServiceProvider`. A `Success` result commits the Inbox row and enlisted business changes together.
`Retry`, `Reject`, and exceptions roll the transaction back, so a later delivery can attempt the work again.

`IInboxStore<TContext>.TryRecordAsync` and `MarkProcessedAsync` remain available as backward-compatible,
low-level primitives. They do not, by themselves, make the Inbox row and business state one atomic unit;
applications that use them directly own transaction orchestration and duplicate-state reconciliation.

## Consistency

- Producer → broker: at-least-once (Outbox guarantees no event lost after a successful commit).
- Consumer database state: once-effective per `(ConsumerName, MessageId)` when the scope-aware handler uses
  the same transaction-owning `TContext` as the atomic Inbox store.
- External side effects cannot participate in that database transaction and may run more than once after a
  broker redelivery or a transient database retry. Make them independently idempotent, or enqueue them through
  an Outbox inside the same transaction.
- Only a committed `Processed` Inbox row is a safe duplicate. A persisted legacy `Received` row is ambiguous:
  the old flow may have recorded it before the handler ran, or business work may have committed before the
  processed marker was written. Reconcile the business state before changing or replaying the row; do not
  blindly replay it or treat it as a safe duplicate.

## Missing features

### Adaptive `OutboxProcessor` poll backoff

Today `OutboxProcessor` polls `IOutboxStore.FetchPendingAsync(...)` on a **fixed** `OutboxOptions.PollInterval`
regardless of whether the previous batch found work. With the default 1s interval that's 86,400 polls/day per
`TContext` per process, and 99%+ of those polls return zero rows in steady state — wasted DB I/O, wasted
connection-pool slots, and noisy SQL command logs.

A smarter pattern: stay at `PollInterval` after a non-empty batch (work might still be coming), back off
exponentially up to a `MaxPollInterval` after N consecutive empty batches, reset to `PollInterval` on the next
non-empty batch. Latency under load stays low; idle steady state is quiet.

**Suggested options shape:**

```csharp
public sealed class OutboxOptions
{
    public TimeSpan PollInterval        { get; set; } = TimeSpan.FromSeconds(1);   // base interval
    public TimeSpan MaxPollInterval     { get; set; } = TimeSpan.FromSeconds(30);  // cap (NEW)
    public int     EmptyBatchesBackoff  { get; set; } = 3;                         // start backing off after N (NEW)
    public double  BackoffMultiplier    { get; set; } = 2.0;                       // exponential factor (NEW)
    // ... existing options
}
```

Until then, callers stuck with the fixed interval should tune `PollInterval` per environment in their
`appsettings.{env}.json`:

```json
"ReliableMessaging": {
  "Outbox": { "PollInterval": "00:00:05" }
}
```
