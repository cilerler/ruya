# Standard Operating Procedure: Reliable Messaging — EF Core Migration Generation

## Purpose

Generate Entity Framework Core migrations for the `OutboxEntry` and `InboxEntry` tables added by `Ruya.Services.ReliableMessaging.EntityFrameworkCore`. This SOP ensures every module follows the same naming, schema, and review conventions so the per-module Outbox/Inbox layout described in the design doc (`../design/reliable-messaging.md`) stays consistent across the codebase.

## Frequency

Ad-hoc — performed once per module when the module first adopts the EF Core reliable messaging adapter, and again whenever `OutboxEntry` / `InboxEntry` mappings change in a future package version.

## Roles Responsible

- **Module owner (Developer)** — runs the migration commands, reviews the generated SQL, and opens the PR.
- **DBA / Reviewer** — reviews migration SQL for correctness, indexes, and schema placement before merge.
- **Documenter** — confirms the README's *Migrations* section still points at this SOP after the package version is bumped.

## Prerequisites

- The module's csproj already references `Ruya.Services.ReliableMessaging.EntityFrameworkCore`.
- The module's `DbContext` calls `modelBuilder.ApplyOutboxEntryConfiguration(...)` and `modelBuilder.ApplyInboxEntryConfiguration(...)` inside `OnModelCreating` (see EF Core adapter README).
- `dotnet-ef` global tool installed (matching the `Microsoft.EntityFrameworkCore.Design` major version pinned in `Directory.Packages.props`).
- Local SQL Server (or LocalDB) reachable for the design-time `DbContext` factory if migrations are scaffolded against a live connection.
- Permission to push a feature branch and open a PR.

## Procedure

### 1. Confirm the schema name

Each module gets its own SQL schema (per the design doc (`../design/reliable-messaging.md`)). Verify that the `SchemaName` passed to `ApplyOutboxEntryConfiguration` / `ApplyInboxEntryConfiguration` matches the module's existing schema (e.g., `Recipe`, `Store`, `Finance`).

```pwsh
Select-String -Path "src\<Module>\<Module>DbContext.cs" -Pattern "ApplyOutboxEntryConfiguration|ApplyInboxEntryConfiguration"
```

**Expected result:** Both calls present, both passing the same `SchemaName` as the rest of the module.

### 2. Generate the migration

Run `dotnet ef migrations add` from the migrations csproj for the module. The migration name should describe what it does — for the initial adoption use `AddReliableMessagingTables`.

- Sub-step a: change into the migrations csproj directory if your repo separates migrations from the runtime project.
- Sub-step b: pass `--context` if the project hosts more than one `DbContext`.
- Sub-step c: pass `--output-dir Migrations/ReliableMessaging` so the new files are colocated and easy to review.

```pwsh
dotnet ef migrations add AddReliableMessagingTables `
    --project src\<Module>.Migrations\<Module>.Migrations.csproj `
    --startup-project src\<Module>.Host\<Module>.Host.csproj `
    --context <Module>DbContext `
    --output-dir Migrations/ReliableMessaging
```

**Expected result:** Two new files created — `<timestamp>_AddReliableMessagingTables.cs` and `<timestamp>_AddReliableMessagingTables.Designer.cs` — plus an updated `<Module>DbContextModelSnapshot.cs`.

### 3. Inspect the generated SQL

Generate the script for the new migration to confirm it matches the design doc (`../design/reliable-messaging.md`) schema expectations (composite PK on Inbox `(ConsumerName, MessageId)`, indexes on Outbox `(Status, NextAttemptAt)` and `EnqueuedAt`).

```pwsh
dotnet ef migrations script --idempotent `
    --project src\<Module>.Migrations\<Module>.Migrations.csproj `
    --startup-project src\<Module>.Host\<Module>.Host.csproj `
    --context <Module>DbContext `
    --output artifacts\<Module>-AddReliableMessagingTables.sql
```

Review the produced script and confirm:

- Tables created in the correct schema (`<Module>.Outbox`, `<Module>.Inbox`).
- Outbox columns and types match the design doc (`../design/reliable-messaging.md`).
- Inbox composite primary key is `(ConsumerName, MessageId)`.
- Indexes `IX_Outbox_Dispatch (Status, NextAttemptAt)`, `IX_Outbox_EnqueuedAt`, and `IX_Inbox_ReceivedAt` are present.

### 4. Commit and open the PR

Commit the migration files, the generated SQL artifact (if your repo policy keeps them), and any updated snapshot file.

```pwsh
git add src\<Module>.Migrations\Migrations\ReliableMessaging\
git commit -m "feat(<module>): add reliable messaging migration"
git push -u origin <branch>
gh pr create --fill
```

Tag the DBA reviewer on the PR and link to the design doc (`../design/reliable-messaging.md`) in the description.

### 5. Verify Completion

After the PR merges and CI deploys to the target environment, confirm the tables exist:

```pwsh
sqlcmd -S "$env:SQL_SERVER" -d "$env:SQL_DATABASE" -Q "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '<Module>' AND TABLE_NAME IN ('Outbox','Inbox') ORDER BY TABLE_NAME;"
```

**Expected result:** Two rows returned, one for each table, in the module's schema.

## Rollback Procedure

If the migration is wrong or causes a deployment failure:

1. Revert the migration locally with `dotnet ef migrations remove --context <Module>DbContext` (only safe if the migration has not yet been applied to a shared environment).
2. If already applied to a shared environment, generate a *down* migration:
   ```pwsh
   dotnet ef migrations script <PreviousMigration> AddReliableMessagingTables --idempotent --output artifacts\rollback.sql
   ```
   and apply the inverse against the target environment after change-management approval.
3. Open a follow-up PR to remove or fix the migration; do not leave a half-applied state.

## Troubleshooting

| Problem | Possible Cause | Resolution |
|---------|---------------|------------|
| `dotnet ef` cannot find the `DbContext` | Wrong `--startup-project` or missing design-time factory | Add an `IDesignTimeDbContextFactory<T>` to the startup project. |
| Generated migration drops unrelated tables | Snapshot file out of sync with current model | Run `dotnet ef migrations remove` and regenerate; never hand-edit the snapshot. |
| Tables created in `dbo` instead of the module schema | `ApplyOutboxEntryConfiguration` called without `SchemaName` (or default) | Pass the module's `SchemaName` explicitly in the options object. |
| Inbox composite PK missing in script | Custom `OnModelCreating` overrode the configuration after `ApplyInboxEntryConfiguration` | Move the `ApplyInboxEntryConfiguration` call to the end of `OnModelCreating`, or remove the conflicting override. |

## Audit Log

PRs adding or modifying reliable messaging migrations must include the generated `.sql` script as an artifact and a link to the design doc (`../design/reliable-messaging.md`). Reviewers leave a checkmark comment when the schema matches the design doc.

## Last Reviewed

2026-04-27

## Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-04-27 | Cengiz Ilerler | Initial version extracted from `Ruya.Services.ReliableMessaging.EntityFrameworkCore` README. |
| 1.1 | 2026-04-27 | Cengiz Ilerler | Updated all references from "ADR-001" to the design doc (`../design/reliable-messaging.md`) after the source document was reclassified from ADR to Design Doc. |
| 1.2 | 2026-04-27 | Cengiz Ilerler | Moved from repo-root `docs/sops/` into `src/Ruya.Services.ReliableMessaging/docs/sops/` so per-project documentation lives with the project; relative links updated. | 
