# Ruya.Services.ReliableMessaging.EntityFrameworkCore

Entity Framework Core storage adapter for `Ruya.Services.ReliableMessaging`.

Provides:

- `EntityFrameworkOutboxStore<TDbContext>` — `IOutboxStore<TDbContext>` implementation
- `EntityFrameworkInboxStore<TDbContext>` — `IAtomicInboxStore<TDbContext>` implementation for the canonical
  consumer path, plus backward-compatible `IInboxStore<TDbContext>` manual primitives
- `OutboxSavingChangesInterceptor<TDbContext>` — drains the outbox buffer during `SaveChangesAsync` so business + outbox rows commit in one transaction
- `ModelBuilder.ApplyOutboxEntryConfiguration` / `ApplyInboxEntryConfiguration` — entity + index mapping
- `DbContextOptionsBuilder.UseReliableMessagingOutbox<TDbContext>(sp)` — attaches the interceptor

## Wiring

```csharp
// Startup / Program.cs
services
    .AddMessageQueue()
    .AddJsonSerializerContext(RecipeContractsJsonSerializerContext.Default)
    .AddRabbitMQ();

services
    .AddReliableMessaging()
    .AddOutboxContext<RecipeDbContext>()
    .AddInboxContext<RecipeDbContext>()
    .AddEntityFrameworkOutboxStore<RecipeDbContext>()
    .AddEntityFrameworkInboxStore<RecipeDbContext>()
    .AddMessageQueueOutboundDispatcher();

services.AddDbContext<RecipeDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.UseReliableMessagingOutbox<RecipeDbContext>(sp);   // attaches the SaveChanges interceptor
});
```

The dispatcher gets its required fallback provider name from
`ReliableMessaging:MessageQueueDispatcher:QueueName`; it must identify an enabled entry in
`MessageQueue:Providers`.

```csharp
// In your DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.ApplyOutboxEntryConfiguration(new EntityFrameworkOutboxStoreOptions
    {
        SchemaName = "Recipe",
        TableName  = "Outbox",
    });

    modelBuilder.ApplyInboxEntryConfiguration(new EntityFrameworkInboxStoreOptions
    {
        SchemaName = "Recipe",
        TableName  = "Inbox",
    });
}
```

After that, your services stay clean:

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

        await outbox.EnqueueSourceGeneratedAsync(
            options.Value.RecipeCreatedEventTopicName,
            new RecipeCreatedEvent(recipe.Id),
            RecipeContractsJsonSerializerContext.Default.RecipeCreatedEvent,
            new OutboxPublishOverrides
            {
                DispatcherName = options.Value.MessageQueueProviderName,
            },
            cancellationToken: ct);

        await db.SaveChangesAsync(ct);   // atomic commit of business row + outbox row
    }
}
```

## Atomic consumer processing

The MessageQueue adapter's scope-aware `SubscribeWithInboxAsync` overload resolves
`IAtomicInboxStore<TDbContext>` and invokes the handler with the same scoped `IServiceProvider`:

```csharp
await using var subscription = await queue.SubscribeWithInboxAsync<RecipeChangedEvent, RecipeDbContext>(
    topic: "recipes.changed",
    consumerName: "Recipe.ChangedHandler",
    scopeFactory: scopeFactory,
    handler: async (services, context) =>
    {
        var db = services.GetRequiredService<RecipeDbContext>();
        // Apply business changes through this scoped DbContext.
        await db.SaveChangesAsync(context.CancellationToken);
        return MessageResult.Success();
    });

await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
```

`Success` commits the `Processed` Inbox row and enlisted business changes together. `Retry`, `Reject`, and
exceptions roll the transaction back. An existing `Processed` row short-circuits as a duplicate. Existing
`Received` or `Failed` rows fail closed because the store cannot prove whether their business effects committed.

The EF execution strategy may invoke the transactional callback more than once after a transient database
failure. Keep database work inside the shared transaction. External side effects cannot be rolled back and must
be independently idempotent; for broker publishes or deferred work, prefer an Outbox row written through the
same `DbContext`.

## Migrations

Generate migrations with `dotnet ef` — the `OutboxEntry` / `InboxEntry` entities are part of your `DbContext`'s model. Follow the per-module conventions in the SOP at [../Ruya.Services.ReliableMessaging/docs/sops/reliable-messaging-ef-migrations.md](../Ruya.Services.ReliableMessaging/docs/sops/reliable-messaging-ef-migrations.md) (naming, schema placement, review checklist).

## Operations

- **Horizontal scaling (multiple pollers).** The default `FetchPendingAsync` uses plain LINQ without SQL Server locking hints. If you scale out hosts that all drain the same Outbox, see [../Ruya.Services.ReliableMessaging/docs/runbooks/reliable-messaging-outbox-horizontal-scaling.md](../Ruya.Services.ReliableMessaging/docs/runbooks/reliable-messaging-outbox-horizontal-scaling.md) for detection and the `UPDLOCK, READPAST` mitigation.

## Inbox conflict handling

The canonical atomic path translates a concurrent insert into a duplicate only after re-reading the persisted
row and confirming that its status is `Processed`. A non-processed row is ambiguous and raises an error; an
insert failure with no matching Inbox row is rethrown instead of being mislabeled as a duplicate.

The backward-compatible `IInboxStore<TDbContext>.TryRecordAsync` primitive retains its original contract: it
translates a `DbUpdateException` on its insert path into `false`. Direct callers must keep the documented Inbox
schema free of additional insert constraints or provide a custom store that distinguishes unique-key violations.

> [!WARNING]
> Before migrating an existing consumer, reconcile every legacy `Received` row against its business state.
> Such a row can mean the handler never ran, the handler failed, or the business commit succeeded before the
> processed marker was saved. Do not blindly replay it, delete it, or count it as a safe duplicate. Mark it
> `Processed` only after confirming the effect committed; remove it for replay only after confirming the effect
> did not commit. Preserve an audit trail appropriate to the affected business data.
