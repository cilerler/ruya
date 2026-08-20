# BatchLock Operations

Provides batch locking operations using the SELECT FOR UPDATE pattern with isolation-aware SQL Server locking hints for concurrent processing.

## Features

- Locks rows in batches for exclusive processing
- Uses `READPAST` at read committed or repeatable read to skip locked rows, enabling parallel workers
- Dynamic SQL generation based on table schema
- Automatic handling of `SoftDelete`, `IsLocked`, and `ProcessStatusCode` columns
- Configurable batch size, lock state, structured status filtering, and ordering field

## Registration

```csharp
// In your startup/program configuration
builder.Services.AddDbContext<AppDbContext>(options => ...);
builder.Services.AddBatchLockOperations<AppDbContext>();
```

## Usage

### Basic Usage

```csharp
public class OrderProcessingService
{
    private readonly IBatchLockOperations _batchLock;

    public OrderProcessingService(IBatchLockOperations batchLock)
    {
        _batchLock = batchLock;
    }

    public async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var options = new BatchLockOptions
        {
            SchemaName = "dbo",
            TableName = "PendingOrders",
            BatchSize = 100,
            LockedBy = "OrderProcessor"
        };

        var query = _batchLock.SelectForUpdate<OrderEntity>(options);
        var lockedRows = await query.AsNoTracking().ToListAsync(cancellationToken);

        foreach (var row in lockedRows)
        {
            // Process each locked row
        }
    }
}
```

### With Process Status Filtering

```csharp
var options = new BatchLockOptions
{
    SchemaName = "dbo",
    TableName = "EmailQueue",
    BatchSize = 100,
    LockedBy = "EmailSender",
    ProcessStatusCodeField = "ProcessStatusCode",
    ProcessStatusCodeValue = 0  // Only process rows with status = 0
};
```

### With a Processing Order Field

```csharp
var options = new BatchLockOptions
{
    SchemaName = "dbo",
    TableName = "JobQueue",
    BatchSize = 25,
    LockedBy = "JobWorker",
    ProcessingOrderField = "Priority"
};
```

The field is resolved against SQL Server metadata and quoted by the embedded query.

### With Trusted Custom SQL

`WhereClause` and `OrderByClause` are escape hatches for fixed SQL authored and reviewed by the application
developer. They are concatenated into the embedded dynamic query and are not parameterized or sanitized.

```csharp
var options = new BatchLockOptions
{
    SchemaName = "dbo",
    TableName = "JobQueue",
    BatchSize = 25,
    LockedBy = "PriorityWorker",
    WhereClause = "t.[Priority] >= 5 AND t.[AvailableAt] <= SYSUTCDATETIME()",
    OrderByClause = "t.[Priority] DESC, t.[AvailableAt] ASC"
};
```

`WhereClause` supplies the predicate without the `WHERE` keyword and replaces all automatically generated
`SoftDelete`, `IsLocked`, and status predicates. `OrderByClause` supplies the expression after `ORDER BY` and
replaces metadata-based ordering; omit the `ORDER BY` keyword. When either property is `null`, its
structured/default behavior remains active.

> **SECURITY RESPONSIBILITY:** These properties must contain only trusted, developer-authored SQL constants.
> Never build them from HTTP input, message payloads, tenant data, configuration controlled by an untrusted
> party, or any other runtime value. The consuming application owns review and safety of the supplied SQL.

### With State Transition

```csharp
var options = new BatchLockOptions
{
    SchemaName = "dbo",
    TableName = "ProcessingQueue",
    BatchSize = 10,
    LockedBy = "WorkerNode",
    ProcessStatusCodeField = "ProcessStatusCode",
    ProcessStatusCodeValue = 0,         // Only lock rows where status is 0
    UpdateProcessStatusCode = true,     // Enable updating the status column
    ProcessStatusCodeNextValue = 1      // Set the status to 1 atomically upon lock
};
```

### Excluding Columns from Result

```csharp
var options = new BatchLockOptions
{
    SchemaName = "dbo",
    TableName = "Documents",
    BatchSize = 100,
    LockedBy = "DocumentProcessor",
    ExcludeFields = "FileContent,Thumbnail,AuditLog"  // Comma-separated
};
```

### Selecting Only Primary Keys

When you only need the IDs of the locked rows, use `SelectForUpdateKeysAsync`:

```csharp
var options = new BatchLockOptions
{
    SchemaName = "dbo",
    TableName = "PendingOrders",
    BatchSize = 100, // Optional, defaults to 1
    LockedBy = "OrderProcessor",
    PrimaryKeyField = "OrderId" // Optional, defaults to "Id"
};

List<long> lockedIds = await _batchLock.SelectForUpdateKeysAsync<long>(options, cancellationToken);
```

## Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SchemaName` | string | `"dbo"` | Database schema name |
| `TableName` | string | required | Target table name |
| `BatchSize` | int? | `1` | Number of rows to lock per batch |
| `LockedBy` | string | required | Identifier of the locking process |
| `LockState` | byte | `1` | Value to set on `LockState` column |
| `LockTime` | DateTime? | UTC now | Timestamp for the lock |
| `ExcludeFields` | string? | null | Comma-separated columns to exclude |
| `WhereClause` | string? | null | Trusted developer-authored predicate replacing generated defaults |
| `OrderByClause` | string? | null | Trusted developer-authored ordering expression replacing generated ordering |
| `ProcessStatusCodeField` | string | `"ProcessStatusCode"` | Status field name |
| `ProcessStatusCodeValue` | byte? | null | Status value to filter by |
| `UpdateProcessStatusCode` | bool | `false` | Enables tracking state transitions during lock |
| `ProcessStatusCodeNextValue`| byte? | null | Target value to update `ProcessStatusCodeField` |
| `ProcessingOrderField` | string | `"ProcessingOrder"` | Order field name |
| `PrimaryKeyField` | string | `"Id"` | Primary key field name |
| `PreserveModifiedAt` | bool | `false` | When true, preserves `ModifiedAt` value instead of updating to avoid trigger issues |
| `OmitModifiedAt` | bool | `false` | When true, omits `ModifiedAt` from the internal SET clause to let SQL triggers manage it |
| `Debug` | bool | `false` | Enables embedded-query diagnostic result sets for local troubleshooting |

## How It Works

1. **CTE Selection**: Selects `TOP(BatchSize)` rows with locking hints compatible with the caller's isolation level
2. **Atomic Update**: Updates `LockState`, `LockTime`, `LockedBy`, and conditionally `ProcessStatusCode` columns
3. **OUTPUT Clause**: Returns all columns of the locked rows

### Default WHERE Conditions

When `WhereClause` is `null`, the following conditions are applied automatically (joined with `AND`) based on column existence:

- `[SoftDelete] = 0` (if `SoftDelete` column exists)
- `[IsLocked] = 0` (if `IsLocked` column exists)
- `[ProcessStatusCode] = @ProcessStatusCodeValue` (if configured and column exists)

If none of these conditions apply, it defaults to `1=1`.

### Locking Hints and Isolation

- The operation preserves the caller's session and transaction isolation level.
- At read committed with `READ_COMMITTED_SNAPSHOT` enabled, it uses `READCOMMITTEDLOCK, UPDLOCK, READPAST`.
- At read committed without RCSI, or at repeatable read, it uses `ROWLOCK, UPDLOCK, READPAST`.
- At isolation levels where SQL Server does not permit `READPAST`, it uses `ROWLOCK, UPDLOCK`; those calls may wait for a conflicting lock.
- `READPAST` skips row/key locks, not page locks. For predictable parallel-worker throughput, index the structured filter and processing-order fields so SQL Server can seek the next eligible row instead of scanning and locking a page.

## Error Handling

Since `SelectForUpdate` returns `IQueryable<T>`, errors occur when the query is materialized:

```csharp
try
{
    var query = _batchLock.SelectForUpdate<MyEntity>(options);
    var results = await query.ToListAsync(cancellationToken);
}
catch (SqlException ex) when (ex.Number == 1205)
{
    // Deadlock - retry
    _logger.LogWarning("Deadlock detected, retrying...");
}
catch (SqlException ex)
{
    _logger.LogError(ex, "Database error during batch lock");
    throw;
}
```

## Table Requirements

The target table must have:

- A configured key column (defaults to `Id`) that is the sole key of a non-filtered unique index. The CTE joins on this field, so uniqueness is required to keep an update within `BatchSize`.
- At least **one** updatable column to mark locked rows from the following list:
  - `LockState` (tinyint)
  - `LockTime` (datetime2)
  - `LockedBy` (varchar)
  - `ModifiedAt` (datetime2) - Unless `OmitModifiedAt` is set to true
  - Or another field configured with `UpdateProcessStatusCode = true`

Optional columns that enable automatic filtering or tracking:

- `ModifiedAt` (datetime2) - Automatically updated unless `OmitModifiedAt` or `PreserveModifiedAt` is set.
- `SoftDelete` (bit)
- `IsLocked` (bit)
- `ProcessStatusCode` (tinyint)
- `ProcessingOrder` (int)

Explicitly configured status-filter, status-update, and ordering fields must exist. Missing fields fail before any row is updated instead of silently dropping the requested behavior. SQL identifiers are limited to SQL Server's 128-character identifier limit.

For parallel queue processing, add an index whose leading keys cover the configured status filter and processing order, for example `(ProcessStatusCode, ProcessingOrder)`.
