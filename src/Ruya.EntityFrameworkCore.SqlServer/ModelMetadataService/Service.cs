using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Ruya.EntityFrameworkCore.ModelMetadata;

public interface IModelMetadata
{
	List<ColumnDefinition> ColumnDefinitions { get; }
}

public partial class ModelMetadataService<TContext> : IModelMetadata where TContext : DbContext
{
	public const string DefaultSchema = "dbo";

    public List<ColumnDefinition> ColumnDefinitions => _columnDefinitions.Value;
    private readonly Lazy<List<ColumnDefinition>> _columnDefinitions;

    private readonly string _defaultSchema;

	/// <summary>
	/// Creates a ModelMetadataService that resolves DbContext from DI.
	/// Requires TContext to be registered in the service collection.
	/// </summary>
	public ModelMetadataService(IServiceScopeFactory scopeFactory, string defaultSchema = DefaultSchema)
	{
        _defaultSchema = defaultSchema;
		_columnDefinitions = new Lazy<List<ColumnDefinition>>(() =>
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
            return GenerateColumnDefinitions(dbContext);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
	}

	/// <summary>
	/// Creates a ModelMetadataService without requiring DbContext in DI.
	/// Uses an empty SQL Server connection to build the EF Core model in memory.
	/// TContext must have a constructor that accepts DbContextOptions&lt;TContext&gt;.
	/// </summary>
	public ModelMetadataService(string defaultSchema = DefaultSchema)
	{
		_defaultSchema = defaultSchema;
		_columnDefinitions = new Lazy<List<ColumnDefinition>>(() =>
		{
			var options = new DbContextOptionsBuilder<TContext>()
				.UseSqlServer(string.Empty)
				.Options;
			using var dbContext = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
			return GenerateColumnDefinitions(dbContext);
		}, LazyThreadSafetyMode.ExecutionAndPublication);
	}

	private List<ColumnDefinition> GenerateColumnDefinitions(DbContext context)
	{
		var output = new List<ColumnDefinition>();

		foreach (IEntityType entityType in context.Model.GetEntityTypes())
		{
			TableAttribute? tableAttribute = entityType.ClrType.GetCustomAttribute<TableAttribute>(false);
			string tableName = tableAttribute?.Name ?? entityType.ClrType.Name;
			string schema = tableAttribute?.Schema ?? _defaultSchema;

			IKey? primaryKey = entityType.FindPrimaryKey();
			HashSet<string> primaryKeyPropertyNames = primaryKey?.Properties.Select(x => x.Name).ToHashSet() ?? new HashSet<string>();

			// Get all EF Core navigations for this entity
			var navigations = entityType.GetNavigations().ToDictionary(n => n.Name);

			// Process all CLR properties
			foreach (PropertyInfo property in entityType.ClrType.GetProperties())
			{
				// Check if this is a navigation property
				if (navigations.TryGetValue(property.Name, out INavigation? navigation))
				{
					// This is a navigation property
					InversePropertyAttribute? inverseProperty = property.GetCustomAttribute<InversePropertyAttribute>(false);
					IForeignKey foreignKey = navigation.ForeignKey;

					output.Add(new ColumnDefinition
					{
						Schema = schema,
						TableName = tableName,
						ColumnName = property.Name,
						// ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
						IsPrimaryKey = foreignKey?.Properties.Any(p => primaryKeyPropertyNames.Contains(p.Name)) ?? false,
						IsNullable = foreignKey?.Properties.Any(p => p.IsNullable) ?? true,  // Navigation is nullable if its FK is nullable
						PropertyName = property.Name,
						ModelType = entityType.ClrType.AssemblyQualifiedName ?? entityType.Name,
						PropertyType = GetCleanTypeName(property.PropertyType),
						IsNavigation = true,
						IsCollection = GetIsCollection(navigation),
						ForeignKeyPropertyName = foreignKey?.Properties.FirstOrDefault()?.Name,
						InversePropertyName = inverseProperty?.Property,
						InverseTypeName = navigation.TargetEntityType.Name
					});
				}
				else
				{
					// This is a regular property, check if it's an actual column
					IProperty? efProperty = entityType.FindProperty(property.Name);
					if (efProperty == null)
						continue;

					ColumnAttribute? columnAttribute = property.GetCustomAttribute<ColumnAttribute>(false);


					// Extract precision and scale from TypeName if it exists
					int? precision = null;
					int? scale = null;

					// Try to get precision and scale from EF Core metadata
					if (efProperty.GetPrecision().HasValue)
					{
						precision = efProperty.GetPrecision();
					}

					if (efProperty.GetScale().HasValue)
					{
						scale = efProperty.GetScale();
					}

					// If not found in metadata, try to extract from TypeName attribute
					if ((!precision.HasValue || !scale.HasValue) && columnAttribute?.TypeName != null)
					{
						// Parse TypeName like "decimal(13, 5)"
						string typeName = columnAttribute.TypeName;
						if (typeName.StartsWith("decimal(") || typeName.StartsWith("numeric("))
						{
							Match match = DecimalPrecisionRegex().Match(typeName);
							if (match.Success)
							{
								// Get precision from group 1 or 3 (depending on decimal or numeric)
								string precisionStr = match.Groups[1].Value;
								if (string.IsNullOrEmpty(precisionStr))
									precisionStr = match.Groups[3].Value;

								// Get scale from group 2 or 4 (depending on decimal or numeric)
								string scaleStr = match.Groups[2].Value;
								if (string.IsNullOrEmpty(scaleStr))
									scaleStr = match.Groups[4].Value;

								if (int.TryParse(precisionStr, out int p))
									precision = p;

								if (int.TryParse(scaleStr, out int s))
									scale = s;
							}
						}
					}

					output.Add(new ColumnDefinition
					{
						Schema = schema,
						TableName = tableName,
						ColumnName = columnAttribute?.Name ?? property.Name,
						ValueGenerated = efProperty.ValueGenerated.ToString(),
						IsPrimaryKey = primaryKeyPropertyNames.Contains(property.Name),
						IsNullable = efProperty.IsNullable,
						PropertyName = property.Name,
						ModelType = entityType.ClrType.AssemblyQualifiedName ?? entityType.Name,
						PropertyType = GetCleanTypeName(property.PropertyType),
						IsNavigation = false,
						Precision = precision,
						Scale = scale
					});
				}
			}
		}

		return output;
	}
	private string? GetCleanTypeName(Type type)
	{
		// Check if it's a nullable type
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
		{
			// Return the underlying type name for nullable types
			return Nullable.GetUnderlyingType(type)?.FullName ?? type.FullName;
		}
		return type.FullName;
	}

	/// <summary>
	/// Gets the IsCollection value from INavigation using reflection to support both EF Core 8 and 10.
	/// In EF Core 8, IsCollection was on IReadOnlyNavigationBase. In EF Core 10, the interface hierarchy changed.
	/// Using reflection ensures binary compatibility across versions.
	/// </summary>
	/// <remarks>
	/// TODO: When this library is upgraded to target .NET 10 / EF Core 10, remove this method
	/// and replace the call site with direct property access: navigation.IsCollection
	/// </remarks>
	private static bool GetIsCollection(INavigation navigation)
	{
		PropertyInfo? isCollectionProperty = navigation.GetType().GetProperty("IsCollection", BindingFlags.Public | BindingFlags.Instance);
		return isCollectionProperty?.GetValue(navigation) as bool? ?? false;
	}

	[GeneratedRegex(@"decimal\((\d+),\s*(\d+)\)|numeric\((\d+),\s*(\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DecimalPrecisionRegex();
}
