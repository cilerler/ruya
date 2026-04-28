# Ruya.Services.ReliableMessaging.EntityFrameworkCore

Entity Framework Core storage adapter for `Ruya.Services.ReliableMessaging`.

Provides:

- `EntityFrameworkOutboxStore<TDbContext>` — `IOutboxStore<TDbContext>` implementation
- `EntityFrameworkInboxStore<TDbContext>` — `IInboxStore<TDbContext>` implementation
- `OutboxSavingChangesInterceptor<TDbContext>` — drains the outbox buffer during `SaveChangesAsync` so business + outbox rows commit in one transaction
- `ModelBuilder.ApplyOutboxEntryConfiguration` / `ApplyInboxEntryConfiguration` — entity + index mapping
- `DbContextOptionsBuilder.UseReliableMessagingOutbox<TDbContext>(sp)` — attaches the interceptor

## Wiring

```csharp
// Startup / Program.cs
services
    .AddReliableMessaging()
    .AddOutboxContext<RecipeDbContext>()
    .AddInboxContext<RecipeDbContext>()
    .AddEntityFrameworkOutboxStore<RecipeDbContext>()
    .AddEntityFrameworkInboxStore<RecipeDbContext>();

// A separate adapter package supplies IOutboundDispatcher:
// services.AddMessageQueueOutboundDispatcher();

services.AddDbContext<RecipeDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.UseReliableMessagingOutbox<RecipeDbContext>(sp);   // attaches the SaveChanges interceptor
});
```

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
    IOutboxPublisher<RecipeDbContext> outbox) : IRecipeService
{
    public async Task CreateAsync(RecipeCreateCommand cmd, CancellationToken ct)
    {
        var recipe = Recipe.CreateFrom(cmd);
        db.Recipes.Add(recipe);

        await outbox.EnqueueAsync("recipes.created", new RecipeCreatedEvent(recipe.Id), ct: ct);

        await db.SaveChangesAsync(ct);   // atomic commit of business row + outbox row
    }
}
```

## Migrations

Generate migrations with `dotnet ef` — the `OutboxEntry` / `InboxEntry` entities are part of your `DbContext`'s model. Follow the per-module conventions in the SOP at [../Ruya.Services.ReliableMessaging/docs/sops/reliable-messaging-ef-migrations.md](../Ruya.Services.ReliableMessaging/docs/sops/reliable-messaging-ef-migrations.md) (naming, schema placement, review checklist).

## Operations

- **Horizontal scaling (multiple pollers).** The default `FetchPendingAsync` uses plain LINQ without SQL Server locking hints. If you scale out hosts that all drain the same Outbox, see [../Ruya.Services.ReliableMessaging/docs/runbooks/reliable-messaging-outbox-horizontal-scaling.md](../Ruya.Services.ReliableMessaging/docs/runbooks/reliable-messaging-outbox-horizontal-scaling.md) for detection and the `UPDLOCK, READPAST` mitigation.

## Inbox `TryRecordAsync` and `DbUpdateException`

`EntityFrameworkInboxStore<TDbContext>.TryRecordAsync` translates the unique-key violation on `(ConsumerName, MessageId)` into a `false` return. This relies on the schema documented in the design doc having no other constraints that could raise `DbUpdateException` on the insert path. If you extend the Inbox schema with extra constraints, see the design doc's *Inbox `DbUpdateException` handling* section ([../Ruya.Services.ReliableMessaging/docs/design/reliable-messaging.md](../Ruya.Services.ReliableMessaging/docs/design/reliable-messaging.md)) for the supported override pattern.
