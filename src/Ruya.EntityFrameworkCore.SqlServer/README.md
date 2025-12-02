# Ruya.EntityFrameworkCore.SqlServer

SQL Server extensions for Entity Framework Core, featuring high-performance bulk operations and metadata services.

## Features

-   **Bulk Insert**: High-performance `SqlBulkCopy` integration with EF Core transactions.
-   **EnumerableDataReader**: A lightweight, streaming `IDataReader` for `IEnumerable<T>`.
-   **ModelMetadataService**: Service to retrieve EF Core entity metadata.
-   **Distributed Tracing**: Automatic OpenTelemetry instrumentation for bulk operations.

## Configuration

### 1. Register Services

```csharp
// Program.cs
builder.Services.AddDbContext<MyDbContext>(options => 
    options.UseSqlServer(connectionString));

// Add Bulk Operations and Metadata Services
builder.Services.AddBulkOperations();
builder.Services.AddDistributedTracingService(); // Optional, for telemetry
```

### 2. Configure Settings (Optional)

Add to `appsettings.json`:

```json
{
  "BulkOperations": {
    "DefaultTimeout": 60,
    "BatchSize": 5000
  }
}
```

## Usage

### 1. Bulk Insert

High-performance insert for large datasets.

```csharp
public class ImportService
{
    private readonly MyDbContext _context;

    public ImportService(MyDbContext context)
    {
        _context = context;
    }

    public async Task ImportProductsAsync(List<Product> products)
    {
        // Auto-detects table and columns from DbContext
        await _context.BulkInsertAsync(products);
    }

    public async Task ImportWithTransactionAsync(List<Order> orders, List<OrderItem> items)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.BulkInsertAsync(orders);
            await _context.BulkInsertAsync(items);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

### 2. EnumerableDataReader

Wraps any `IEnumerable<T>` as an `IDataReader`, useful for `SqlBulkCopy` or other streaming operations.

```csharp
var products = new List<Product> { ... };
var columns = new[] { "Id", "Name", "Price" };

using var reader = new EnumerableDataReader<Product>(products, columns);

// Use with SqlBulkCopy
using var bulkCopy = new SqlBulkCopy(connectionString);
bulkCopy.DestinationTableName = "Products";
await bulkCopy.WriteToServerAsync(reader);
```

### 3. ModelMetadataService

Retrieve metadata about your EF Core entities, such as table names and column mappings.

```csharp
public class MetadataService
{
    private readonly IModelMetadataService _metadataService;
    private readonly MyDbContext _context;

    public MetadataService(IModelMetadataService metadataService, MyDbContext context)
    {
        _metadataService = metadataService;
        _context = context;
    }

    public void PrintTableInfo()
    {
        var tableName = _metadataService.GetTableName(_context, typeof(Product));
        var columns = _metadataService.GetColumnNames(_context, typeof(Product));
        
        Console.WriteLine($"Table: {tableName}");
        foreach (var col in columns)
        {
            Console.WriteLine($" - {col}");
        }
    }
}
```

## Advanced Bulk Insert Options

```csharp
await _context.BulkInsertAsync(products, options =>
{
    options.BatchSize = 10000;
    options.Timeout = 120;
    options.TableLock = true;     // Lock table for faster inserts
    options.FireTriggers = false; // Disable triggers
    options.KeepIdentity = true;  // Insert explicit identity values
});
```

## Parallel Bulk Insert

For very large datasets, use parallel inserts with `IDbContextFactory`:

```csharp
public async Task ParallelImportAsync(
    IDbContextFactory<MyDbContext> contextFactory,
    IEnumerable<Product> products)
{
    var chunks = products.Chunk(10_000);

    await Parallel.ForEachAsync(chunks, async (chunk, ct) =>
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await context.BulkInsertAsync(chunk, ct);
    });
}
```
