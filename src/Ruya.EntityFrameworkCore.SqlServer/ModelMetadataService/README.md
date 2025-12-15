# ModelMetadataService

Provides EF Core entity metadata extraction including table names, column mappings, navigation properties, and data type information.

## Features

- Extract table and column metadata from EF Core model
- Support for primary keys, nullable columns, and value generation
- Navigation property detection with foreign key mappings
- Decimal precision and scale extraction
- Lazy initialization with thread-safe caching
- Generic DbContext support

## Registration

```csharp
// In your startup/program configuration
builder.Services.AddDbContext<AppDbContext>(options => ...);
builder.Services.AddModelMetadataService<AppDbContext>();
```

## Usage

### Basic Usage

```csharp
public class SchemaInspectorService
{
    private readonly IModelMetadata _metadata;

    public SchemaInspectorService(IModelMetadata metadata)
    {
        _metadata = metadata;
    }

    public void PrintSchema()
    {
        foreach (var column in _metadata.ColumnDefinitions)
        {
            Console.WriteLine($"{column.Schema}.{column.TableName}.{column.ColumnName}");
            Console.WriteLine($"  Property: {column.PropertyName} ({column.PropertyType})");
            Console.WriteLine($"  Primary Key: {column.IsPrimaryKey}");
            Console.WriteLine($"  Nullable: {column.IsNullable}");
        }
    }
}
```

### Filter by Table

```csharp
public IEnumerable<ColumnDefinition> GetColumnsForTable(string tableName)
{
    return _metadata.ColumnDefinitions
        .Where(c => c.TableName == tableName && !c.IsNavigation);
}
```

### Get Primary Key Columns

```csharp
public IEnumerable<ColumnDefinition> GetPrimaryKeyColumns(string tableName)
{
    return _metadata.ColumnDefinitions
        .Where(c => c.TableName == tableName && c.IsPrimaryKey);
}
```

### Get Navigation Properties

```csharp
public IEnumerable<ColumnDefinition> GetNavigations(string tableName)
{
    return _metadata.ColumnDefinitions
        .Where(c => c.TableName == tableName && c.IsNavigation);
}
```

### Get Decimal Columns with Precision

```csharp
public IEnumerable<ColumnDefinition> GetDecimalColumns(string tableName)
{
    return _metadata.ColumnDefinitions
        .Where(c => c.TableName == tableName
            && c.PropertyType == "System.Decimal"
            && c.Precision.HasValue);
}
```

## ColumnDefinition Properties

| Property | Type | Description |
|----------|------|-------------|
| `Schema` | string | Database schema name |
| `TableName` | string | Table name |
| `ColumnName` | string | Column name in database |
| `PropertyName` | string | CLR property name |
| `PropertyType` | string? | Full CLR type name |
| `ModelType` | string | Entity type assembly-qualified name |
| `IsPrimaryKey` | bool | Whether column is part of primary key |
| `IsNullable` | bool | Whether column allows nulls |
| `ValueGenerated` | string? | EF Core value generation strategy |
| `Precision` | int? | Decimal precision (if applicable) |
| `Scale` | int? | Decimal scale (if applicable) |
| `IsNavigation` | bool | Whether this is a navigation property |
| `IsCollection` | bool | Whether navigation is a collection |
| `ForeignKeyPropertyName` | string? | Foreign key property name |
| `InversePropertyName` | string? | Inverse navigation property name |
| `InverseTypeName` | string? | Target entity type name |

## How It Works

1. **Lazy Initialization**: Metadata is generated on first access and cached
2. **Thread Safety**: Uses `Lazy<T>` with `ExecutionAndPublication` mode
3. **Scope Factory**: Creates a temporary scope to access DbContext for metadata extraction
4. **Attribute Support**: Respects `[Table]`, `[Column]`, and `[InverseProperty]` attributes
