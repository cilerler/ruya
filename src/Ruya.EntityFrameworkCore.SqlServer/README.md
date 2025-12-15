# Ruya.EntityFrameworkCore.SqlServer

SQL Server extensions for Entity Framework Core.

## Modules

| Module | Description |
|--------|-------------|
| [BatchLock](./BatchLock/README.md) | Batch locking with `SELECT FOR UPDATE` pattern using `ROWLOCK`, `UPDLOCK`, `READPAST` hints |
| [BulkInsert](./BulkInsert/README.md) | High-performance `SqlBulkCopy` integration with EF Core transactions |
| [ModelMetadataService](./ModelMetadataService/README.md) | EF Core entity metadata extraction (tables, columns, navigation properties) |

## Utilities

### EnumerableDataReader

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
