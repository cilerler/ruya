# BatchLock Operations

Provides batch locking operations using the SELECT FOR UPDATE pattern with SQL Server hints (`ROWLOCK`, `UPDLOCK`, `READPAST`) for concurrent processing without blocking.

## Features

- Locks rows in batches for exclusive processing
- Uses `READPAST` to skip locked rows, enabling parallel workers
- Dynamic SQL generation based on table schema
- Automatic handling of `SoftDelete`, `IsLocked`, and `ProcessStatusCode` columns
- Configurable batch size, lock state, and filtering

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

### With Custom WHERE Clause

```csharp
var options = new BatchLockOptions
{
    SchemaName = "dbo",
    TableName = "TaskQueue",
    BatchSize = 50,
    LockedBy = "TaskWorker",
    WhereClause = "t.[Priority] > 5 AND t.[CreatedAt] < DATEADD(hour, -1, GETUTCDATE())"
};
```

> **WARNING**: Custom `WhereClause` and `OrderByClause` values must be validated to prevent SQL injection.

### With Custom ORDER BY

```csharp
var options = new BatchLockOptions
{
    SchemaName = "dbo",
    TableName = "JobQueue",
    BatchSize = 25,
    LockedBy = "JobWorker",
    OrderByClause = "t.[Priority] DESC, t.[CreatedAt] ASC"
};
```

> **NOTE**: If no custom `OrderByClause` is specified, the `ProcessingOrder` field is used by default if it exists. If neither are available, no ordering is enforced by SQL Server.

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
| `WhereClause` | string? | null | Custom WHERE clause (use with caution) |
| `OrderByClause` | string? | null | Custom ORDER BY clause |
| `ProcessStatusCodeField` | string | `"ProcessStatusCode"` | Status field name |
| `ProcessStatusCodeValue` | byte? | null | Status value to filter by |
| `UpdateProcessStatusCode` | bool | `false` | Enables tracking state transitions during lock |
| `ProcessStatusCodeNextValue`| byte? | null | Target value to update `ProcessStatusCodeField` |
| `ProcessingOrderField` | string | `"ProcessingOrder"` | Order field name |
| `PrimaryKeyField` | string | `"Id"` | Primary key field name |
| `PreserveModifiedAt` | bool | `false` | When true, preserves `ModifiedAt` value instead of updating to avoid trigger issues |
| `OmitModifiedAt` | bool | `false` | When true, omits `ModifiedAt` from the internal SET clause to let SQL triggers manage it |

## How It Works

1. **CTE Selection**: Selects `TOP(BatchSize)` rows with `ROWLOCK, UPDLOCK, READPAST` hints
2. **Atomic Update**: Updates `LockState`, `LockTime`, `LockedBy`, and conditionally `ProcessStatusCode` columns
3. **OUTPUT Clause**: Returns all columns of the locked rows

### Default WHERE Conditions

If `WhereClause` is not provided, the following conditions are applied automatically (joined with `AND`) based on column existence:

- `[SoftDelete] = 0` (if `SoftDelete` column exists)
- `[IsLocked] = 0` (if `IsLocked` column exists)
- `[ProcessStatusCode] = @ProcessStatusCodeValue` (if configured and column exists)

If none of these conditions apply, it defaults to `1=1`.

### Locking Hints

- `ROWLOCK`: Lock at row level (not page/table)
- `UPDLOCK`: Acquire update lock (prevents other updates)
- `READPAST`: Skip rows locked by other transactions

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

- A Primary Key column (defaults to `Id`, used for joining in the UPDATE).
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
