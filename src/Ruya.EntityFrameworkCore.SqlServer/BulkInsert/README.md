# BulkInsert Operations

High-performance bulk insert operations using `SqlBulkCopy` with EF Core transaction support.

## Features

- High-performance `SqlBulkCopy` integration with EF Core
- Automatic table and column detection from DbContext
- Transaction support (participates in existing transactions)
- Configurable batch size, timeout, and copy options
- Progress notification callbacks
- OpenTelemetry distributed tracing instrumentation

## Registration

```csharp
// In your startup/program configuration
builder.Services.AddDbContext<AppDbContext>(options => ...);
builder.Services.AddDistributedTracingService(); // Required for telemetry
builder.Services.AddBulkInsertOperations<AppDbContext>();
```

## Configuration

Add to `appsettings.json`:

```json
{
  "BulkInsertOperations": {
    "Timeout": 60,
    "BatchSize": 5000
  }
}
```

## Usage

### Basic Usage (Auto-detect from DbContext)

```csharp
public class ProductImportService
{
    private readonly AppDbContext _context;

    public ProductImportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task ImportProductsAsync(List<Product> products, CancellationToken ct)
    {
        // Auto-detects table name and columns from DbContext
        await _context.BulkInsertAsync(products, ct);
    }
}
```

### With Explicit Table and Columns

```csharp
public async Task ImportAsync(
    IBulkInsertOperations bulkInsert,
    AppDbContext context,
    IEnumerable<OrderDto> orders,
    CancellationToken ct)
{
    await bulkInsert.BulkInsertAsync(
        context,
        orders,
        tableName: "dbo.Orders",
        columns: new[] { "Id", "CustomerId", "OrderDate", "Total" },
        ct);
}
```

### With Full Configuration Options

```csharp
await bulkInsert.BulkInsertAsync(context, products, new BulkInsertOptions
{
    TableName = "dbo.Products",
    Columns = new[] { "Id", "Name", "Price", "CategoryId" },
    BatchSize = 10000,
    Timeout = 120,
    TableLock = true,      // Lock table for faster inserts
    FireTriggers = false,  // Disable triggers
    KeepIdentity = true,   // Insert explicit identity values
    CheckConstraints = true,
    KeepNulls = false
}, ct);
```

### With Transaction

```csharp
public async Task ImportWithTransactionAsync(
    List<Order> orders,
    List<OrderItem> items,
    CancellationToken ct)
{
    await using var transaction = await _context.Database.BeginTransactionAsync(ct);
    try
    {
        await _context.BulkInsertAsync(orders, ct);
        await _context.BulkInsertAsync(items, ct);
        await transaction.CommitAsync(ct);
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}
```

### With Progress Notification

```csharp
await bulkInsert.BulkInsertAsync(context, largeDataset, new BulkInsertOptions
{
    BatchSize = 10000,
    NotifyAfterRows = 5000,  // Notify every 5000 rows
    NotifyAfter = rowsCopied =>
    {
        _logger.LogInformation("Copied {RowsCopied} rows...", rowsCopied);
    }
}, ct);
```

### Parallel Bulk Insert

For very large datasets, use parallel inserts with `IDbContextFactory`:

```csharp
public async Task ParallelImportAsync(
    IDbContextFactory<AppDbContext> contextFactory,
    IEnumerable<Product> products,
    CancellationToken ct)
{
    var chunks = products.Chunk(10_000);

    await Parallel.ForEachAsync(chunks, ct, async (chunk, token) =>
    {
        await using var context = await contextFactory.CreateDbContextAsync(token);
        await context.BulkInsertAsync(chunk, token);
    });
}
```

## Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TableName` | string? | auto | Destination table name (auto-detected if null) |
| `Columns` | string[]? | auto | Column names to map (auto-detected if null) |
| `BatchSize` | int | `1000` | Rows per batch sent to server |
| `Timeout` | int | `30` | Timeout in seconds |
| `CheckConstraints` | bool | `true` | Enable CHECK constraints |
| `FireTriggers` | bool | `true` | Fire triggers during copy |
| `KeepIdentity` | bool | `false` | Keep identity values from source |
| `KeepNulls` | bool | `false` | Keep nulls instead of defaults |
| `TableLock` | bool | `false` | Lock table for better performance |
| `NotifyAfterRows` | int? | null | Rows between progress notifications |
| `NotifyAfter` | Action<long>? | null | Progress callback |

## Service Settings

Settings configured at startup via `appsettings.json`:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Timeout` | int | `30` | Default timeout in seconds |
| `BatchSize` | int | `1000` | Default batch size |

## Transactions and Retries

Each call participates in the current EF Core transaction when one exists. Without a caller transaction, the operation creates one so a later failing `SqlBulkCopy` batch rolls back earlier batches from the same call.

Bulk insert is not inherently idempotent, so the operation does not automatically invoke an EF Core retry execution strategy. This remains true when the `DbContext` has `EnableRetryOnFailure` configured. A caller may implement an operation-specific retry only when it can verify whether an ambiguous attempt committed or otherwise prevent duplicate rows.
