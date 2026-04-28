# Tech Spec Design Doc: Reliable Messaging — Transactional Outbox + Inbox

## Metadata

**Date:** 2026-04-21
**Status:** Implemented
**Authors:** Cengiz Ilerler
**Reviewers:** Cengiz Ilerler

### Status Definitions

| Status | Meaning |
|--------|---------|
| DRAFT | Still under development; feedback and changes expected. |
| FINAL | Design agreed upon; implementation can begin or is in progress. |
| IMPLEMENTED | Design has been implemented in production. |
| OBSOLETE | Design is no longer applicable; superseded or discarded. |

## Context and Scope

Distributed and modular systems need *reliable messaging*: events emitted by one service must reach consumers even under crashes, broker failures, and retries. Two symmetric problems dominate:

1. **Producer-side loss.** A service writes business state and then publishes an event. If it crashes between the commit and the publish, the broker never hears — the event is lost, and consumers diverge from the source of truth.

2. **Consumer-side duplication.** Brokers typically deliver at-least-once. A consumer that has already processed a message may receive it again after a transient failure or restart. Without dedup, the consumer applies the same change twice.

The industry pattern that solves both is **Transactional Outbox** (producer) paired with **Inbox** (consumer). Together they deliver effectively-exactly-once semantics across services without distributed transactions.

`Ruya.Services.MessageQueue` provides a provider-agnostic messaging abstraction with a middleware pipeline, but no outbox / inbox primitives. Modular-monolith and microservice consumers alike need both: every module publishes crash-safely and consumes idempotently.

This design implements one pattern library under `Ruya.Services.*` covering both sides, plus two adapter packages (EF Core and MessageQueue). The pattern library does not depend on `Ruya.Services.MessageQueue` or on any persistence stack. Integration is by adapter. `Ruya.Services.MessageQueue` itself is unchanged.

### Goals

- Deliver effectively-exactly-once semantics between modules without distributed transactions.
- Keep the pattern library persistence-agnostic and transport-agnostic.
- Make the producer call site read clearly: enqueue, then commit.
- Allow per-`DbContext` (or per-marker `TContext`) isolation so each module owns its Outbox/Inbox.
- Leave `Ruya.Services.MessageQueue` untouched and remain non-breaking for existing consumers.
- Compose with any future store (Dapper, Azure Tables, …) and any future dispatcher (HTTP, Kafka, Service Bus, …).

### Non-Goals

- **Saga / workflow orchestration.** Use Durable Task Framework; Outbox/Inbox is for stateless domain events.
- **Fan-out to multiple dispatchers from one outbox row.** Current model: one row, one destination. A future `FanOut` dispatcher wrapper can compose if needed.
- **Cross-service distributed transactions.** Outbox+Inbox is the explicit choice to avoid them.
- **Strict ordering across topics.** Within a topic, FIFO is best-effort.

## Overview

Three packages ship as the implementation:

```
Ruya.Services.ReliableMessaging                      — pattern + abstractions (no EF, no MessageQueue)
Ruya.Services.ReliableMessaging.EntityFrameworkCore  — EF store adapters + SaveChangesInterceptor
Ruya.Services.ReliableMessaging.MessageQueue         — IOutboundDispatcher bridge + subscriber-scoped Inbox dedup
```

Each adapter depends only on the pattern package plus one ecosystem (EF Core or `Ruya.Services.MessageQueue`). The pattern package has zero knowledge of EF or MessageQueue.

The producer-side flow:

1. Caller injects `IOutboxPublisher<TDbContext>` and the `TDbContext`.
2. Caller enqueues via `outbox.EnqueueAsync(topic, payload, ...)`. The publisher serializes and adds an envelope to a scoped `IOutboxBuffer<TDbContext>`.
3. Caller calls `db.SaveChangesAsync(ct)`. The EF `OutboxSavingChangesInterceptor<TDbContext>` drains the buffer into the change tracker as `OutboxEntry` rows; the business row and the outbox row commit atomically.
4. `OutboxProcessor` (an `IHostedService`) polls `IOutboxStore<TDbContext>`, dispatches each envelope through `IOutboundDispatcher`, and marks rows dispatched (or schedules retry).

The consumer-side flow:

1. The host subscribes via `IMessageQueue.SubscribeWithInboxAsync<TMessage, TDbContext>(...)`.
2. On every message, the extension creates a DI scope, resolves `IInboxStore<TDbContext>`, and calls `TryRecordAsync(consumerName, messageId)` — atomic per-pair via composite primary key.
3. If the row is new, the user handler runs inside the same scope and commits its business write together with the inbox row in `SaveChangesAsync`. If the handler rolls back, the inbox row rolls back and redelivery will re-dedup correctly.
4. If the row is a duplicate, the handler is short-circuited with `MessageResult.Success()` — at-least-once collapses to once-effective.

## Detailed Design

### Component: `Ruya.Services.ReliableMessaging` (pattern package)

Shared primitives:

- `ReliableMessageEnvelope` — record: `MessageId` (Guid), `Topic`, `DispatcherName?`, `PayloadJson`, `PayloadType` (assembly-qualified), `Headers`, `EnqueuedAt`.
- `IOutboundDispatcher` — abstraction: *"send this envelope somewhere."* Implementations decide the destination. The MessageQueue adapter supplies one; users can write others (direct HTTP, Azure Service Bus SDK, Kafka, etc.).

Outbox primitives (under `Outbox/`):

- `IOutboxBuffer<TContext>` (scoped) — per-scope list of pending envelopes. `Add(ReliableMessageEnvelope)` during the unit of work; the store adapter drains it when the unit of work commits.
- `IOutboxStore<TContext>` — abstraction: *"persist these envelopes atomically with the caller's unit of work, and fetch pending envelopes for dispatch."* Implementations decide storage.
- `IOutboxPublisher<TContext>` — **explicit primary API** for callers: `EnqueueAsync(string topic, T payload, ReliableMessageOptions? options = null, CancellationToken ct = default)`. Wraps `IOutboxBuffer.Add` with serialization + envelope construction.
- `OutboxProcessor<TContext>` (`IHostedService`) — polls `IOutboxStore<TContext>`, calls `IOutboundDispatcher.DispatchAsync`, marks dispatched or schedules retry. Exponential backoff, configurable poll interval, batch size, max attempts.
- `OutboxOptions` — `PollInterval`, `BatchSize`, `MaxAttempts`, `BackoffSchedule`, `ArchiveAfter`, `DefaultDispatcherName?`.
- `OutboxEntry` — persistence record (see schema section).

Inbox primitives (under `Inbox/`):

- `IInboxStore<TContext>` — abstraction with two operations:
  - `TryRecordAsync(string consumerName, string messageId, CancellationToken)` → `bool`. Atomic per `(consumerName, messageId)`: returns `true` on first seen, `false` on duplicate. Implementations rely on the composite primary key for atomicity (insert-or-fail).
  - `MarkProcessedAsync(string consumerName, string messageId)` — optional transition from Received → Processed. Enables reprocessing if a crash occurs between receive and business commit.
- `IInboxConsumerNameProvider` — **default implementation auto-derives the consumer name from handler type** (returns `typeof(THandler).FullName`). Override by registering a custom implementation or by applying `[InboxConsumerName("custom.name")]` to the handler class. Precedence: attribute → custom provider → convention.
- `InboxOptions` — `ArchiveAfter` (TTL for processed rows), `RequireExplicitProcessed` (bool, default false), `CleanupInterval`.
- `InboxCleanupProcessor<TContext>` (`IHostedService`) — deletes processed rows older than `ArchiveAfter`.
- `InboxEntry` — persistence record (see schema section).

### Component: `Ruya.Services.ReliableMessaging.EntityFrameworkCore`

- `EntityFrameworkOutboxStore<TDbContext>` implements `IOutboxStore<TDbContext>` using `TDbContext`.
- `OutboxSavingChangesInterceptor<TDbContext>` hooks `DbContextOptionsBuilder`. In `SavingChangesAsync`, drains `IOutboxBuffer<TDbContext>` into `TDbContext`'s change tracker as `OutboxEntry` rows. They commit in the same transaction as business entities; rollback drops them.
- `EntityFrameworkInboxStore<TDbContext>` implements `IInboxStore<TDbContext>`. `TryRecordAsync` does a conditional insert (catches `DbUpdateException` on unique-key violation → returns false). See "Inbox `DbUpdateException` handling" below for the constraint rule.
- `ModelBuilder.ApplyOutboxEntryConfiguration` / `ApplyInboxEntryConfiguration` — entity + index mapping helpers consumed from the module's `OnModelCreating`.
- `AddEntityFrameworkOutboxStore<TDbContext>(options)` / `AddEntityFrameworkInboxStore<TDbContext>(options)` — registration extensions.

Consumers wanting Dapper, raw ADO, Azure Tables, or a different store implement `IOutboxStore<TContext>` / `IInboxStore<TContext>` directly — the pattern library doesn't care.

#### Inbox `DbUpdateException` handling

`EntityFrameworkInboxStore<TDbContext>.TryRecordAsync` performs an *insert-or-fail* against the composite primary key `(ConsumerName, MessageId)`. Atomicity depends on the database raising a unique-key violation when a duplicate is attempted, which EF Core surfaces as `DbUpdateException`.

The adapter catches `DbUpdateException` from this code path and translates it into a `false` return value (duplicate observed). This is correct **only as long as the Inbox table has no other constraints that could raise `DbUpdateException` during the insert**. If a deployment adds a `CHECK` constraint, additional unique index, or foreign key on the Inbox table, those violations would also be swallowed and reported as duplicates — masking real bugs.

**Decision rule for adopters:** keep the Inbox table to the schema documented below (composite PK + the listed columns and indexes). If a module needs additional Inbox constraints, replace the default store with a custom `IInboxStore<TDbContext>` implementation that inspects `DbUpdateException.InnerException` (e.g., SQL Server error number `2627` / `2601` for unique-key violations) and rethrows on any other error class.

### Component: `Ruya.Services.ReliableMessaging.MessageQueue`

- `MessageQueueOutboundDispatcher` implements `IOutboundDispatcher`. Resolves `IMessageQueue` via `IMessageQueueFactory.CreateQueueAsync(envelope.DispatcherName ?? options.QueueName)` and calls `PublishAsync<TMessage>`. Uses cached reflection to invoke the generic method because the concrete payload type is only known from persisted metadata; the `MethodInfo` is cached per payload type.
- `IMessageQueue.SubscribeWithInboxAsync<TMessage, TDbContext>(...)` extension — subscriber-scoped Inbox dedup. Resolves the consumer name via `IInboxConsumerNameProvider` (or an explicit string), creates a DI scope per message, calls `IInboxStore<TDbContext>.TryRecordAsync(consumerName, envelope.MessageId)`; if duplicate → returns `MessageResult.Success()` without invoking the handler.
- `AddMessageQueueOutboundDispatcher()` — registration.

The package does **not** install a pipeline-wide MessageQueue middleware for Inbox dedup. A pipeline-wide middleware cannot know the `(ConsumerName, TDbContext)` pair for a given message — both vary per subscription. Subscriber-scoped dedup via `SubscribeWithInboxAsync<TMessage, TDbContext>(...)` lets each subscription pick its own pair, which is what the modular-monolith / multi-`DbContext` shape requires. (See *Alternatives Considered → Pipeline-wide middleware for Inbox dedup*.)

### Primary caller surface — explicit

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

The intent is readable on the call site: enqueue, then commit. The `SaveChangesInterceptor` flushes the buffer into the context transparently; no ambient state magic.

### Consumer surface — auto-derived name

```csharp
public sealed class RecipeChangedHandler(StoreDbContext db) : IMessageHandler<RecipeChangedEvent>
{
    public async Task<MessageResult> HandleAsync(MessageContext<RecipeChangedEvent> context, CancellationToken ct)
    {
        // SubscribeWithInboxAsync has already dedup'd on (consumerName, messageId).
        // consumerName defaults to "Store.Modules.RecipeSnapshot.RecipeChangedHandler"
        // (full type name) unless overridden via attribute or IInboxConsumerNameProvider.
        db.RecipeSnapshots.Update(/* ... */);
        await db.SaveChangesAsync(ct);
        return MessageResult.Success();
    }
}

// Override when needed:
[InboxConsumerName("Store.RecipeSnapshotProjector")]
public sealed class RecipeChangedHandler(...) : IMessageHandler<RecipeChangedEvent> { ... }
```

Convention covers the common case; attribute + custom provider cover rename-safety and special identities.

### Registration example (host composition root, modular monolith with multiple `DbContext`s)

```csharp
services.AddMessageQueue(configuration)
    .AddRabbitMQ(...)
    .AddMsSql(...);

services
    .AddReliableMessaging(options =>
    {
        options.Outbox.PollInterval = TimeSpan.FromSeconds(1);
        options.Outbox.BatchSize    = 100;
        options.Outbox.MaxAttempts  = 10;
        options.Inbox.ArchiveAfter  = TimeSpan.FromDays(7);
    })
    .AddOutboxContext<RecipeDbContext>()
    .AddOutboxContext<StoreDbContext>()
    .AddOutboxContext<FinanceDbContext>()
    .AddInboxContext<RecipeDbContext>()
    .AddInboxContext<StoreDbContext>()
    .AddInboxContext<FinanceDbContext>()
    .AddEntityFrameworkOutboxStore<RecipeDbContext>(o => o.SchemaName = "Recipe")
    .AddEntityFrameworkOutboxStore<StoreDbContext>(o  => o.SchemaName = "Store")
    .AddEntityFrameworkOutboxStore<FinanceDbContext>(o => o.SchemaName = "Finance")
    .AddEntityFrameworkInboxStore<RecipeDbContext>(o => o.SchemaName = "Recipe")
    .AddEntityFrameworkInboxStore<StoreDbContext>(o  => o.SchemaName = "Store")
    .AddEntityFrameworkInboxStore<FinanceDbContext>(o => o.SchemaName = "Finance")
    .AddMessageQueueOutboundDispatcher();
```

The root builder `AddReliableMessaging(options)` returns an `IReliableMessagingBuilder` exposing chainable `AddOutboxContext<T>()`, `AddInboxContext<T>()`, `AddEntityFrameworkOutboxStore<T>()`, `AddEntityFrameworkInboxStore<T>()`, `AddMessageQueueOutboundDispatcher()`, etc.

### Schema — per-context

EF migrations generate these inside each module's migrations csproj, in the module's own schema. See [the EF migration SOP](../sops/reliable-messaging-ef-migrations.md) for the standard procedure.

**`{Module}.Outbox`:**

| Column | Type | Notes |
|---|---|---|
| Id | `UNIQUEIDENTIFIER` | PK = message id |
| Topic | `NVARCHAR(255)` | |
| DispatcherName | `NVARCHAR(64) NULL` | null → default dispatcher |
| PayloadJson | `NVARCHAR(MAX)` | serialized payload |
| PayloadType | `NVARCHAR(512)` | assembly-qualified type name |
| Headers | `NVARCHAR(MAX) NULL` | JSON |
| EnqueuedAt | `DATETIME2` | |
| DispatchedAt | `DATETIME2 NULL` | |
| NextAttemptAt | `DATETIME2` | scheduled retry time |
| AttemptCount | `INT` | |
| LastError | `NVARCHAR(MAX) NULL` | |
| Status | `TINYINT` | 0=Pending, 1=Dispatched, 2=Poisoned |

Indices: `IX_Outbox_Dispatch (Status, NextAttemptAt)`, `IX_Outbox_EnqueuedAt`.

**`{Module}.Inbox`:**

| Column | Type | Notes |
|---|---|---|
| ConsumerName | `NVARCHAR(256)` | part of composite PK |
| MessageId | `NVARCHAR(64)` | part of composite PK |
| Topic | `NVARCHAR(255)` | |
| ReceivedAt | `DATETIME2` | |
| ProcessedAt | `DATETIME2 NULL` | |
| Status | `TINYINT` | 0=Received, 1=Processed, 2=Failed |

PK = `(ConsumerName, MessageId)` — composite uniqueness gives atomic dedup via insert-or-fail. Index `IX_Inbox_ReceivedAt` supports TTL cleanup.

### Consistency model

- **Producer → broker:** at-least-once. Outbox survives crashes; processor retries until broker confirms.
- **Broker → consumer:** at-least-once (broker-dependent; existing `Ruya.Services.MessageQueue` behaviour).
- **Consumer effective semantics:** exactly-once per `(ConsumerName, MessageId)`, enforced by Inbox.
- **Atomicity of business + outbox write:** guaranteed by EF `SaveChangesAsync` (both rows in one transaction).
- **Atomicity of receive + inbox write:** the consumer subscription wraps the handler so that `TryRecord` and the business write land in the same transaction; if the handler rolls back, the inbox row rolls back, and redelivery will re-dedup correctly.
- **No ordering guarantees across topics.** Within a topic, FIFO is best-effort.

### Relationship to Other Systems

- **`Ruya.Services.MessageQueue`** — consumed by the MessageQueue adapter via `IMessageQueueFactory` and the existing middleware pipeline. The messaging package is unchanged and unaware of reliable-messaging concerns.
- **EF Core** — consumed by the EF Core adapter via `TDbContext`, `SaveChangesInterceptor`, and `ModelBuilder` extensions.
- **Host composition root** — wires both adapters via the chainable `IReliableMessagingBuilder` returned by `AddReliableMessaging(...)`.
- **Module migrations csprojs** — own the EF migrations for `OutboxEntry` / `InboxEntry`; see the SOP.

## Cross-Cutting Concerns

### Security

- The Outbox table holds serialized payloads. If payloads contain sensitive data (PII, credentials), the table inherits the database's at-rest encryption (TDE) and access controls; no additional encryption layer is added by this design.
- `PayloadType` stores assembly-qualified type names, which can leak internals to anyone with read access on the Outbox table. Restrict table access to the application service account.
- The EF adapter uses parameterized queries via EF Core's expression translation; the optional raw-SQL store described in the horizontal-scaling runbook also parameterizes the `batchSize`. There is no user-controlled string in the dispatch path.

### Privacy

- Payloads are user-defined types serialized to JSON. The library does not redact or classify fields; adopters that store PII in events should apply tokenization or field-level encryption *before* `EnqueueAsync` and reverse it in handlers.
- Inbox rows store `ConsumerName` and `MessageId` only — no payload content. They retain for `ArchiveAfter` (default 7 days) before `InboxCleanupProcessor` deletes them.

### Scalability

- **Vertical:** single-poller throughput is bounded by `BatchSize` * (1 / `PollInterval`). Tuning these and the dispatcher's parallelism handles low-millions-per-day per `TDbContext`.
- **Horizontal:** multiple hosts polling the same Outbox can race for rows. The default LINQ `FetchPendingAsync` does not use SQL Server locking hints; under scale-out, swap the store for a `UPDLOCK, READPAST` raw-SQL implementation. See [the horizontal-scaling runbook](../runbooks/reliable-messaging-outbox-horizontal-scaling.md).
- **Inbox table growth:** TTL via `InboxCleanupProcessor`; high-volume consumers should tune `ArchiveAfter` and the supporting index `IX_Inbox_ReceivedAt`.

### Monitoring

OpenTelemetry metrics and activity spans:

- **Outbox:** `outbox.pending_count` (gauge), `outbox.dispatched_total` (counter), `outbox.failures_total` (counter, tag `reason`), `outbox.dispatch_duration` (histogram). Activity `Outbox.Dispatch` (kind=Producer).
- **Inbox:** `inbox.received_total` (counter, tags `consumer`, `outcome=first|duplicate`), `inbox.processed_total`, `inbox.cleanup_total`. Activity `Inbox.Receive` (kind=Consumer).

Health checks: pending-count and poisoned-count thresholds surface via `IHealthCheck` implementations in the pattern package.

## Alternatives Considered

### Alternative 1: Keep Outbox and Inbox as separate package families

Callers in a modular monolith always need both, so two packages add ceremony with no real upside. Two packages would require extra references and version coordination for no functional benefit; the two halves don't call each other and collocating them has negligible coupling cost. Rejected for unnecessary surface area.

### Alternative 2: Put patterns under `Ruya.Services.MessageQueue.*`

Mislabels Outbox/Inbox as messaging-layer concerns; they are persistence-side reliability primitives that pair with messaging. Locks the patterns to the current messaging stack. Rejected for layering.

### Alternative 3: Add DbContext / EF Core awareness into `Ruya.Services.MessageQueue`

Pollutes the messaging layer with persistence concerns and forces an EF Core dependency on every consumer of the messaging package. Rejected for layering.

### Alternative 4: Transparent publish as the primary API (middleware diverts `PublishAsync` to the buffer)

Same call site behaves differently depending on whether an `IOutboxBuffer` is resolvable; surprising and bug-prone, and obscures the durability boundary at the call site. `IOutboxPublisher.EnqueueAsync(topic, payload)` is the explicit primary API instead.

### Alternative 5: `ConsumerName` as a required explicit registration parameter

Forces every handler to declare a string. Boilerplate that teams will skip or misconfigure. Rejected in favor of convention-plus-override (auto-derive from handler type with `[InboxConsumerName]` and custom `IInboxConsumerNameProvider` overrides).

### Alternative 6: Adopt MediatR / MassTransit instead

Adds a heavyweight external dependency and a programming model that duplicates `Ruya.Services.MessageQueue` abstractions. Larger surface area than required for the Outbox/Inbox problem. Implementation uses only `Microsoft.Extensions.*` + `Microsoft.EntityFrameworkCore` (in the EF adapter).

### Alternative 7: Outbox now, Inbox later

At-least-once delivery without Inbox forces dedup into every handler forever; teams forget consumer-side dedup; bugs follow at the worst time. Shipping together makes the correct default the easy default.

### Alternative 8: Pipeline-wide MessageQueue middleware for consumer-side Inbox dedup

A pipeline-wide middleware does not know the `(ConsumerName, TDbContext)` pair for a given message — both vary per subscription. Forces the middleware to resolve a default `TDbContext` and a derived `ConsumerName`, which is wrong whenever a host has more than one persistence boundary or more than one logical consumer. Subscriber-scoped dedup via `SubscribeWithInboxAsync<TMessage, TDbContext>(...)` lets each subscription pick its own pair, which is what the modular monolith actually needs.

## Metrics

| Metric | Target | How Measured |
|--------|--------|--------------|
| Producer event loss after successful business commit | 0 events / month | Compare counts of business rows with corresponding Outbox rows; integration test asserts atomic commit + rollback. |
| Consumer duplicate-apply rate | 0 duplicates applied to business state | `inbox.received_total{outcome=duplicate}` rises while business state stays consistent; verified via end-to-end integration sample with deliberate consumer crashes. |
| Outbox dispatch latency (P50) | < 2 × `PollInterval` | `outbox.dispatch_duration` histogram. |
| Outbox poisoned-row rate | < 0.1% of dispatched | `outbox.failures_total{reason=*}` over `outbox.dispatched_total`. |
| Inbox table size growth | bounded by `ArchiveAfter` window | Row count snapshot vs. expected steady-state given throughput and TTL. |

## Timeline

| Phase | Target Date | Description |
|-------|-------------|-------------|
| Design Finalization | 2026-04-21 | This document. |
| Implementation — pattern package | 2026-04-22 | `Ruya.Services.ReliableMessaging`: envelope, dispatcher abstraction, Outbox/Inbox primitives, options, hosted services. |
| Implementation — EF Core adapter | 2026-04-23 | `Ruya.Services.ReliableMessaging.EntityFrameworkCore`: stores, interceptor, model-builder helpers, registration extensions. |
| Implementation — MessageQueue adapter | 2026-04-24 | `Ruya.Services.ReliableMessaging.MessageQueue`: dispatcher, `SubscribeWithInboxAsync`. |
| Health checks + OpenTelemetry | 2026-04-25 | Instrumentation in the pattern package. |
| End-to-end integration sample | 2026-04-26 | Recipe → Store with deliberate producer/consumer crashes; verifies no event loss and no duplicate application. |
| Rollout | 2026-04-27 | Packages tagged; READMEs, runbook, and SOP published. |

## Operations

- **Horizontal scaling (multiple pollers).** Detection and `UPDLOCK, READPAST` mitigation: [horizontal-scaling runbook](../runbooks/reliable-messaging-outbox-horizontal-scaling.md).
- **Per-module EF migration generation.** Standard procedure with naming, schema placement, and review checklist: [EF migration SOP](../sops/reliable-messaging-ef-migrations.md).

## References

- [Ruya.Services.ReliableMessaging README](../../README.md)
- [Ruya.Services.ReliableMessaging.EntityFrameworkCore README](../../../Ruya.Services.ReliableMessaging.EntityFrameworkCore/README.md)
- [Ruya.Services.ReliableMessaging.MessageQueue README](../../../Ruya.Services.ReliableMessaging.MessageQueue/README.md)
- Ruya.Services.MessageQueue README — `src/Ruya.Services.MessageQueue/README.md` (existing middleware + idempotency patterns).
- Chris Richardson — *Pattern: Transactional Outbox*, microservices.io.
- Kamil Grzybek — *The Outbox Pattern* and *The Inbox Pattern*, industry write-ups.

## Revision History

| Version | Date | Status | Change Description | Author(s) |
|---------|------|--------|-------------------|-----------|
| 1.0 | 2026-04-21 | Draft | Initial design (originally drafted as ADR-001). | Cengiz Ilerler |
| 1.1 | 2026-04-27 | Implemented | Restructured to the design-doc template; status flipped to Implemented to reflect that all three packages, the runbook, and the SOP are in the repo. Folded prior ADR addenda and considered options into the design body. | Cengiz Ilerler |
| 1.2 | 2026-04-27 | Implemented | Moved from repo-root `docs/design/` into `src/Ruya.Services.ReliableMessaging/docs/design/` so per-project documentation lives with the project; relative links updated. | Cengiz Ilerler |
