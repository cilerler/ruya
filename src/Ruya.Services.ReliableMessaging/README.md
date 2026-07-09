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
    .AddReliableMessaging(options =>
    {
        options.Outbox.PollInterval = TimeSpan.FromSeconds(1);
        options.Outbox.BatchSize    = 100;
        options.Outbox.MaxAttempts  = 10;
        options.Inbox.ArchiveAfter  = TimeSpan.FromDays(7);
    })
    .AddOutboxContext<RecipeDbContext>()
    .AddInboxContext<RecipeDbContext>();

// Storage adapter — Entity Framework Core:
// services.AddEntityFrameworkOutboxStore<RecipeDbContext>(o => o.SchemaName = "Recipe");
// services.AddEntityFrameworkInboxStore<RecipeDbContext>(o  => o.SchemaName = "Recipe");

// Outbound dispatcher — Ruya.Services.MessageQueue:
// services.AddMessageQueueOutboundDispatcher();
```

## Caller surface

```csharp
public sealed class RecipeService(
    RecipeDbContext db,
    IOutboxPublisher<RecipeDbContext> outbox) : IRecipeService
{
    public async Task CreateAsync(RecipeCreateCommand cmd, CancellationToken ct)
    {
        var recipe = Recipe.CreateFrom(cmd);
        db.Recipes.Add(recipe);

        await outbox.EnqueueAsync("recipes.created", new RecipeCreatedEvent(recipe.Id), ct: ct);

        await db.SaveChangesAsync(ct);   // atomic: business row + outbox row
    }
}
```

The `SaveChangesInterceptor` provided by the storage adapter drains the outbox buffer inside `SaveChangesAsync`,
so the outbox row and business state commit in the same transaction. Rollback drops both.

Consumer-side dedup is handled by the transport adapter's middleware (e.g. `InboxConsumeMiddleware` in
`Ruya.Services.ReliableMessaging.MessageQueue`), which invokes `IInboxStore.TryRecordAsync` before the handler.

## Consistency

- Producer → broker: at-least-once (Outbox guarantees no event lost after a successful commit).
- Consumer: effectively exactly-once per `(ConsumerName, MessageId)` enforced by the Inbox composite PK.
- Handlers must be idempotent; the Inbox dedup handles the vast majority, and idempotent business logic handles the edge.

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
