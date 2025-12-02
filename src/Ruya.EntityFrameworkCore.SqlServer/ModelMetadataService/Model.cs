namespace Ruya.EntityFrameworkCore.ModelMetadata;

public record ColumnDefinition
{
	public required string Schema { get; set; }
	public required string TableName { get; set; }
	public required string ColumnName { get; set; }
	public string? ValueGenerated { get; set; }
	public bool IsPrimaryKey { get; set; }
	public bool IsNullable { get; set; }
	public required string PropertyName { get; set; }
	public required string ModelType { get; set; }
	public required string? PropertyType { get; set; }

	// Decimal specific fields
	public int? Precision { get; set; }
	public int? Scale { get; set; }

	// Navigation property specific fields
	public bool IsNavigation { get; set; }
	public bool IsCollection { get; set; }
	public string? ForeignKeyPropertyName { get; set; }
	public string? InversePropertyName { get; set; }
	public string? InverseTypeName { get; set; }
}
