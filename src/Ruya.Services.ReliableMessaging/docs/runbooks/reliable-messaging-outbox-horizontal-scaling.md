# Runbook: Reliable Messaging — Outbox Horizontal Scaling (Multiple Pollers)

## Purpose

Operate `Ruya.Services.ReliableMessaging.EntityFrameworkCore` when the host is scaled horizontally and **multiple instances poll the same Outbox table concurrently**. The default `EntityFrameworkOutboxStore<TDbContext>.FetchPendingAsync` implementation uses plain LINQ and does **not** use SQL Server locking hints, which can cause two pollers to claim the same row and dispatch the same envelope twice within the at-least-once window.

This runbook documents how to detect the problem, mitigate it on SQL Server with `UPDLOCK, READPAST` row-level locking, and roll back if the mitigation regresses.

## Prerequisites

- SQL Server backing store for the affected `TDbContext` (the locking hints below are SQL-Server-specific).
- Access to the application repository and permission to merge a custom `IOutboxStore<TDbContext>` implementation.
- Access to the operational dashboards for `outbox.dispatched_total` and `outbox.failures_total` (per the design doc's Monitoring section).
- Familiarity with `Ruya.Services.ReliableMessaging` and the design doc at [../design/reliable-messaging.md](../design/reliable-messaging.md).

## Steps

> **Note:** Use PowerShell syntax in all shell commands. Do not use Unix-style commands like `grep`.

### 1. Confirm the symptom (duplicate dispatch under scale-out)

Before mitigating, verify that duplicate dispatch is actually happening. Symptoms include consumers reporting `(consumerName, messageId)` collisions in their Inbox (i.e., `TryRecordAsync` returns `false` repeatedly for the same message id) and `outbox.dispatched_total` rising faster than expected.

Query the database to see whether the same Outbox row is being marked dispatched twice or whether the dispatch counter exceeds row count:

```pwsh
sqlcmd -S "$env:SQL_SERVER" -d "$env:SQL_DATABASE" -Q "SELECT TOP 50 Id, Status, DispatchedAt, AttemptCount FROM [Recipe].[Outbox] WHERE Status = 1 ORDER BY DispatchedAt DESC;"
```

**Expected output:** rows with `Status=1` (Dispatched) and a single `DispatchedAt`. If you see suspicious gaps (e.g., consumers report duplicates while the table itself shows single-dispatch), the duplication is happening between fetch and update — exactly what `UPDLOCK, READPAST` solves.

### 2. Replace the default store with a SQL Server locking implementation

Add a custom `IOutboxStore<TDbContext>` for the affected `DbContext` that issues a raw SQL fetch with `UPDLOCK, READPAST` so each poller claims a disjoint set of rows.

```csharp
public sealed class SqlServerLockingOutboxStore<TDbContext>(TDbContext db)
    : IOutboxStore<TDbContext> where TDbContext : DbContext
{
    public async Task<IReadOnlyList<OutboxEntry>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        // UPDLOCK + READPAST = "lock the rows you take, skip rows another poller has locked".
        // Combined with the IX_Outbox_Dispatch (Status, NextAttemptAt) index, this serializes
        // claim-without-blocking across N pollers.
        var sql = $@"
            SELECT TOP ({batchSize}) *
            FROM [{SchemaName}].[{TableName}] WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE Status = 0 AND NextAttemptAt <= SYSUTCDATETIME()
            ORDER BY NextAttemptAt;";

        return await db.Set<OutboxEntry>()
            .FromSqlRaw(sql)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    // MarkDispatchedAsync, ScheduleRetryAsync, etc. — delegate to base or reimplement as needed.
}
```

Register it in place of the EF default for the contexts that scale out:

```csharp
services.AddScoped<IOutboxStore<RecipeDbContext>, SqlServerLockingOutboxStore<RecipeDbContext>>();
```

### 3. Deploy and observe

Deploy the change to a single host first. Watch for:

- `outbox.dispatched_total` no longer climbs faster than rows leaving `Status=0`.
- `outbox.failures_total{reason=*}` does not spike (the `READPAST` skip should not surface as failure).
- No new lock-wait timeouts in SQL Server (`sys.dm_exec_requests` showing `LCK_M_*` waits).

```pwsh
sqlcmd -S "$env:SQL_SERVER" -d "$env:SQL_DATABASE" -Q "SELECT wait_type, COUNT(*) AS waits FROM sys.dm_exec_requests WHERE wait_type LIKE 'LCK_%' GROUP BY wait_type;"
```

**Expected output:** zero rows or low single-digit counts. Sustained `LCK_M_U` or `LCK_M_X` waits mean the locking pattern is too coarse for your workload — open an incident and roll back.

### 4. Scale out and re-verify

Once one host is healthy, scale to the original instance count and re-run the verification queries from step 1 and step 3. Duplicate dispatches should drop to zero on the consumer-side Inbox metrics (`inbox.received_total{outcome=duplicate}` should fall by an order of magnitude or more).

### 5. Check Monitoring

- Visit the Outbox dashboard (per the design doc's Monitoring section).
- Verify: `outbox.pending_count` drains at a steady rate; `outbox.failures_total` stays flat; `inbox.received_total{outcome=duplicate}` returns to baseline.

## Rollback Plan

If the locking implementation causes lock-wait timeouts, deadlocks, or throughput regression:

1. Revert the DI registration to the default EF store:
   ```pwsh
   git revert <commit-sha-of-locking-store-registration>
   ```

2. Redeploy to all instances.

3. **Temporarily scale to a single poller instance** (set replicas to 1 for the host running `OutboxProcessor`) until a better locking strategy is implemented. The default at-least-once semantics + Inbox dedup remain correct with a single poller; only throughput is affected.

4. Escalate to the maintainer of `Ruya.Services.ReliableMessaging` to design a better claim-and-dispatch strategy.

## Escalation

| Condition | Contact | Method |
|-----------|---------|--------|
| If unresolved after 30 minutes and the broker backlog is growing | Platform on-call | Slack |
| If consumers report business-data corruption from duplicate apply | Module owner of the consuming module | Slack + page |
| If SQL Server deadlocks appear | DBA | Slack |

## Contact

- **Primary:** Cengiz Ilerler (`@cilerler` on Slack) — maintainer of `Ruya.Services.*`
- **Backup:** Platform team (`#platform`)

## Related Documentation

- Design doc: [../design/reliable-messaging.md](../design/reliable-messaging.md)
- Pattern README: [../../README.md](../../README.md)
- EF Core adapter README: [../../../Ruya.Services.ReliableMessaging.EntityFrameworkCore/README.md](../../../Ruya.Services.ReliableMessaging.EntityFrameworkCore/README.md)
- SQL Server locking hints reference: Microsoft Docs — *Table Hints (Transact-SQL)*
