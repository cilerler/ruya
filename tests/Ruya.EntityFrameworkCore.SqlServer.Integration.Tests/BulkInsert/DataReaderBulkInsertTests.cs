using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;
using Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests;

/// <summary>
/// Integration tests for the IDataReader bulk insert overload.
/// This overload is designed for scenarios like importing from Parquet files,
/// another database, or CSV where you already have an IDataReader.
/// </summary>
[TestClass]
public class DataReaderBulkInsertTests
{
	private static SqlServerFixture _fixture = null!;
	private ServiceProvider _serviceProvider = null!;
	private TestDbContext _context = null!;
	private IBulkInsertOperations _bulkOperations = null!;

	[ClassInitialize]
	public static async Task ClassInitialize(TestContext _)
	{
		_fixture = new SqlServerFixture();
		await _fixture.InitializeAsync();
	}

	[ClassCleanup]
	public static async Task ClassCleanup()
	{
		await _fixture.DisposeAsync();
	}

	[TestInitialize]
	public async Task TestInitialize()
	{
		_serviceProvider = _fixture.CreateServiceProvider();
		_context = _serviceProvider.GetRequiredService<TestDbContext>();
		_bulkOperations = _serviceProvider.GetRequiredService<IBulkInsertOperations>();

		await _fixture.CleanTablesAsync();
		await SeedCategoriesAsync();
	}

	[TestCleanup]
	public async Task TestCleanup()
	{
		await _context.DisposeAsync();
		await _serviceProvider.DisposeAsync();
	}

	private async Task SeedCategoriesAsync()
	{
		await using var seedContext = _fixture.CreateDbContext();
		var categories = TestDataGenerator.CreateCategories(5);
		seedContext.Categories.AddRange(categories);
		await seedContext.SaveChangesAsync();
	}

	#region Basic IDataReader Insert Tests

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_SingleRow_InsertsSuccessfully()
	{
		// Arrange
		var data = new List<ProductImportDto>
		{
			TestDataGenerator.CreateProductImportDto(1, 1)
		};
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};

		// Act
		var result = await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.CountAsync();
		Assert.AreEqual(1, inserted);
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_MultipleRows_InsertsAllSuccessfully()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(100, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};

		// Act
		var result = await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.CountAsync();
		Assert.AreEqual(100, inserted);
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_LargeDataset_InsertsSuccessfully()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(5000, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};

		// Act
		var result = await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.CountAsync();
		Assert.AreEqual(5000, inserted);
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_EmptyReader_ReturnsZeroWithoutError()
	{
		// Arrange
		var data = new List<ProductImportDto>();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};

		// Act
		var result = await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.CountAsync();
		Assert.AreEqual(0, inserted);
	}

	#endregion

	#region Options Tests

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_WithTableLock_InsertsSuccessfully()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(500, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 100,
			TableLock = true
		};

		// Act
		var result = await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.CountAsync();
		Assert.AreEqual(500, inserted);
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_WithProgressCallback_ReportsProgress()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(500, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var progressReports = new List<long>();
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 100,
			NotifyAfterRows = 100,
			NotifyAfter = rowsCopied => progressReports.Add(rowsCopied)
		};

		// Act
		var result = await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		Assert.IsTrue(progressReports.Count > 0, "Should have received progress reports");
		CollectionAssert.AreEqual(progressReports.OrderBy(x => x).ToList(), progressReports, "Progress reports should be in ascending order");
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_WithCustomTimeout_DoesNotTimeout()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(1000, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 500,
			Timeout = 300 // 5 minutes
		};

		// Act
		var result = await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.CountAsync();
		Assert.AreEqual(1000, inserted);
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_WithExplicitColumns_InsertsSuccessfully()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(100, 1).ToList();
		var columns = GetProductColumns();
		using var reader = new ListDataReader<ProductImportDto>(data, columns);
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			Columns = columns,
			BatchSize = 1000
		};

		// Act
		var result = await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.CountAsync();
		Assert.AreEqual(100, inserted);
	}

	#endregion

	#region Transaction Tests

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_WithTransaction_CommitsSuccessfully()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(50, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};
		await using var transaction = await _context.Database.BeginTransactionAsync();

		// Act
		await _bulkOperations.BulkInsertAsync(_context, reader, options);
		await transaction.CommitAsync();

		// Assert
		await using var verifyContext = _fixture.CreateDbContext();
		var count = await verifyContext.Products.CountAsync();
		Assert.AreEqual(50, count);
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_WithTransaction_RollbackDiscardsData()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(50, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};
		await using var transaction = await _context.Database.BeginTransactionAsync();

		// Act
		await _bulkOperations.BulkInsertAsync(_context, reader, options);
		await transaction.RollbackAsync();

		// Assert
		await using var verifyContext = _fixture.CreateDbContext();
		var count = await verifyContext.Products.CountAsync();
		Assert.AreEqual(0, count);
	}

	#endregion

	#region Error Cases

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_MissingTableName_ThrowsException()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(10, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var options = new BulkInsertOptions
		{
			BatchSize = 1000
			// TableName intentionally omitted
		};

		// Act & Assert
		await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await _bulkOperations.BulkInsertAsync(_context, reader, options));
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_CancellationRequested_ThrowsOperationCanceledException()
	{
		// Arrange
		var data = TestDataGenerator.CreateProductImportDtos(10000, 1).ToList();
		using var reader = new ListDataReader<ProductImportDto>(data, GetProductColumns());
		var cts = new CancellationTokenSource();
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 100,
			NotifyAfterRows = 100,
			NotifyAfter = _ => cts.Cancel() // Cancel after first batch
		};

		// Act & Assert
		await Assert.ThrowsAsync<OperationCanceledException>(
			async () => await _bulkOperations.BulkInsertAsync(_context, reader, options, cts.Token));
	}

	#endregion

	#region Data Integrity Tests

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_PreservesAllPropertyValues()
	{
		// Arrange
		var dto = new ProductImportDto
		{
			Name = "Test Product",
			Description = "Test Description",
			Price = 99.99m,
			Quantity = 42,
			IsActive = true,
			CreatedAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
			CategoryId = 1
		};
		using var reader = new ListDataReader<ProductImportDto>(new[] { dto }, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};

		// Act
		await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.FirstAsync();
		Assert.AreEqual("Test Product", inserted.Name);
		Assert.AreEqual("Test Description", inserted.Description);
		Assert.AreEqual(99.99m, inserted.Price);
		Assert.AreEqual(42, inserted.Quantity);
		Assert.IsTrue(inserted.IsActive);
		Assert.AreEqual(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc), inserted.CreatedAt);
		Assert.AreEqual(1, inserted.CategoryId);
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_HandlesNullableProperties()
	{
		// Arrange
		var dto = TestDataGenerator.CreateProductImportDto(1, 1);
		dto.Description = null;
		using var reader = new ListDataReader<ProductImportDto>(new[] { dto }, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};

		// Act
		await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.FirstAsync();
		Assert.IsNull(inserted.Description);
	}

	[TestMethod]
	public async Task BulkInsertAsync_IDataReader_HandlesUnicodeCharacters()
	{
		// Arrange
		var dto = TestDataGenerator.CreateProductImportDto(1, 1);
		dto.Name = "Produit avec émojis 🎉";
		dto.Description = "Description with ünïcödé and 日本語";
		using var reader = new ListDataReader<ProductImportDto>(new[] { dto }, GetProductColumns());
		var options = new BulkInsertOptions
		{
			TableName = "dbo.Products",
			BatchSize = 1000
		};

		// Act
		await _bulkOperations.BulkInsertAsync(_context, reader, options);

		// Assert
		var inserted = await _context.Products.FirstAsync();
		Assert.AreEqual("Produit avec émojis 🎉", inserted.Name);
		Assert.AreEqual("Description with ünïcödé and 日本語", inserted.Description);
	}

	#endregion

	#region Helpers

	private static string[] GetProductColumns() =>
		["Name", "Description", "Price", "Quantity", "IsActive", "CreatedAt", "CategoryId"];

	#endregion
}

/// <summary>
/// Simple IDataReader implementation backed by a List&lt;T&gt; for testing.
/// This simulates the behavior of external data readers like ParquetDataReader.
/// </summary>
internal sealed class ListDataReader<T> : IDataReader where T : class
{
	private readonly IList<T> _data;
	private readonly string[] _columns;
	private readonly Dictionary<string, int> _columnOrdinals;
	private readonly Func<T, int, object?>[] _getters;
	private int _currentIndex = -1;

	public ListDataReader(IList<T> data, string[] columns)
	{
		_data = data;
		_columns = columns;
		_columnOrdinals = columns.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase);
		_getters = BuildGetters(columns);
	}

	private static Func<T, int, object?>[] BuildGetters(string[] columns)
	{
		var type = typeof(T);
		var getters = new Func<T, int, object?>[columns.Length];

		for (var i = 0; i < columns.Length; i++)
		{
			var prop = type.GetProperty(columns[i])
				?? throw new InvalidOperationException($"Property '{columns[i]}' not found on type '{type.Name}'");
			var localIndex = i;
			getters[i] = (obj, _) => prop.GetValue(obj);
		}

		return getters;
	}

	public bool Read()
	{
		_currentIndex++;
		return _currentIndex < _data.Count;
	}

	public int FieldCount => _columns.Length;
	public string GetName(int i) => _columns[i];
	public int GetOrdinal(string name) => _columnOrdinals[name];
	public object GetValue(int i) => _getters[i](_data[_currentIndex], i) ?? DBNull.Value;
	public bool IsDBNull(int i) => GetValue(i) == DBNull.Value;

	public DataTable? GetSchemaTable()
	{
		var schemaTable = new DataTable();
		schemaTable.Columns.Add("ColumnName", typeof(string));
		schemaTable.Columns.Add("ColumnOrdinal", typeof(int));
		schemaTable.Columns.Add("DataType", typeof(Type));

		var type = typeof(T);
		for (var i = 0; i < _columns.Length; i++)
		{
			var prop = type.GetProperty(_columns[i]);
			var row = schemaTable.NewRow();
			row["ColumnName"] = _columns[i];
			row["ColumnOrdinal"] = i;
			row["DataType"] = prop?.PropertyType ?? typeof(object);
			schemaTable.Rows.Add(row);
		}

		return schemaTable;
	}

	// Required IDataReader members
	public void Close() { }
	public void Dispose() { }
	public int Depth => 0;
	public bool IsClosed => false;
	public int RecordsAffected => -1;
	public bool NextResult() => false;

	// Type-specific getters (simplified - in production you'd implement these properly)
	public bool GetBoolean(int i) => (bool)GetValue(i);
	public byte GetByte(int i) => (byte)GetValue(i);
	public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
	public char GetChar(int i) => (char)GetValue(i);
	public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
	public IDataReader GetData(int i) => throw new NotSupportedException();
	public string GetDataTypeName(int i) => GetFieldType(i).Name;
	public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
	public decimal GetDecimal(int i) => (decimal)GetValue(i);
	public double GetDouble(int i) => (double)GetValue(i);
	public Type GetFieldType(int i) => typeof(T).GetProperty(_columns[i])?.PropertyType ?? typeof(object);
	public float GetFloat(int i) => (float)GetValue(i);
	public Guid GetGuid(int i) => (Guid)GetValue(i);
	public short GetInt16(int i) => (short)GetValue(i);
	public int GetInt32(int i) => (int)GetValue(i);
	public long GetInt64(int i) => (long)GetValue(i);
	public string GetString(int i) => (string)GetValue(i);
	public int GetValues(object[] values)
	{
		var count = Math.Min(values.Length, FieldCount);
		for (var i = 0; i < count; i++)
			values[i] = GetValue(i);
		return count;
	}
	public object this[int i] => GetValue(i);
	public object this[string name] => GetValue(GetOrdinal(name));
}
