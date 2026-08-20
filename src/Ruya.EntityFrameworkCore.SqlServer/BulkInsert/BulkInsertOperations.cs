using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.EntityFrameworkCore.ModelMetadata;

namespace Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

/// <summary>
/// High-performance bulk operations using SqlBulkCopy with EF Core integration.
/// </summary>
public sealed class BulkInsertOperations : IBulkInsertOperations
{
	private readonly ILogger<BulkInsertOperations> _logger;
	private readonly IDistributedTracing _tracing;
	private readonly BulkInsertOperationsSettings _settings;
	private readonly IModelMetadata _modelMetadata;

	/// <summary>
	/// Cache key includes entity type and resolved table name to handle different mappings
	/// for the same entity across different DbContext configurations (e.g., multi-tenant schemas).
	/// </summary>
	private readonly ConcurrentDictionary<(Type EntityType, string TableName), string[]> _columnCache = new();
	/// <summary>
	/// Cache for resolved DB column names to avoid repeated reflection.
	/// Key: (EntityType, PropertyName) → DB column name.
	/// </summary>
	private static readonly ConcurrentDictionary<(Type, string), string> _dbColumnNameCache = new();

	public BulkInsertOperations(ILogger<BulkInsertOperations> logger, IDistributedTracing tracing, IOptions<BulkInsertOperationsSettings> options, IModelMetadata modelMetadata)
	{
		_logger = logger;
		_tracing = tracing;
		_settings = options.Value;
		_modelMetadata = modelMetadata;

		Timeout = _settings.Timeout;
		BatchSize = _settings.BatchSize;
	}

	/// <inheritdoc />
	public int Timeout { get; }

	/// <inheritdoc />
	public int BatchSize { get; }

	/// <inheritdoc />
	public async Task<long> BulkInsertAsync<TEntity>(
		DbContext context,
		IEnumerable<TEntity> entities,
		CancellationToken cancellationToken = default) where TEntity : class
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(entities);

		var (tableName, columns) = GetEntityMetadata<TEntity>(context);

		return await BulkInsertAsync(context, entities, tableName, columns, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<long> BulkInsertAsync<T>(
		DbContext context,
		IEnumerable<T> entities,
		string tableName,
		string[] columns,
		CancellationToken cancellationToken = default) where T : class
	{
		var options = new BulkInsertOptions
		{
			TableName = tableName,
			Columns = columns,
			BatchSize = BatchSize,
			Timeout = Timeout
		};

		return await BulkInsertAsync(context, entities, options, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<long> BulkInsertAsync<T>(
		DbContext context,
		IEnumerable<T> entities,
		BulkInsertOptions options,
		CancellationToken cancellationToken = default) where T : class
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(entities);
		ArgumentNullException.ThrowIfNull(options);
		ValidateOptions(options);

		using var activity = _tracing.StartActivity("BulkInsert", ActivityKind.Client);
		activity.SetTag("db.system", "mssql");
		activity.SetTag("db.operation", "BulkInsert");
		activity.SetTag("db.sql.table", options.TableName);

		// Materialize once because callers may provide a single-pass sequence.
		var entityList = entities as IList<T> ?? entities.ToList();

		if (entityList.Count == 0)
		{
			_logger.BulkInsertSkipped();
			return 0;
		}

		activity.SetTag("db.bulk.count", entityList.Count);

		// Resolve table name and columns if not provided
		var tableName = options.TableName;
		var columns = options.Columns;

		if (string.IsNullOrEmpty(tableName) || columns is null || columns.Length == 0)
		{
			var (detectedTable, detectedColumns) = GetEntityMetadata<T>(context);
			tableName ??= detectedTable;
			columns ??= detectedColumns;
		}

		if (string.IsNullOrEmpty(tableName))
		{
			throw new InvalidOperationException($"Could not determine table name for type {typeof(T).Name}. Provide TableName in options.");
		}

		tableName = NormalizeDestinationTableName(tableName);

		if (columns is null || columns.Length == 0)
		{
			throw new InvalidOperationException($"Could not determine columns for type {typeof(T).Name}. Provide Columns in options.");
		}

		_logger.BulkInsertStarted(tableName);
		var stopwatch = Stopwatch.StartNew();

		try
		{
			// Bulk insert is not inherently idempotent. Retrying after an ambiguous commit can duplicate
			// rows and replay progress callbacks, so retry orchestration must remain with a caller that
			// has an operation-specific idempotency or verification contract.
			var connection = context.Database.GetDbConnection() as SqlConnection
				?? throw new InvalidOperationException("BulkInsert requires a SqlConnection. Ensure you're using SQL Server provider.");

			var transaction = context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;

			// A connection can be left Broken after a transient network failure. Treat it like Closed,
			// but preserve an already-open caller connection and its transaction ownership.
			var needsOpen = connection.State is ConnectionState.Closed or ConnectionState.Broken;
			if (needsOpen)
			{
				if (connection.State == ConnectionState.Broken)
				{
					await connection.CloseAsync();
				}
				await connection.OpenAsync(cancellationToken);
			}

			try
			{
				await ExecuteBulkCopyAsync(
					connection,
					transaction,
					entityList,
					tableName,
					columns,
					options,
					cancellationToken);
			}
			finally
			{
				if (needsOpen && connection.State == ConnectionState.Open)
				{
					await connection.CloseAsync();
				}
			}

			var rowsProcessed = (long)entityList.Count;

			stopwatch.Stop();

			activity.SetTag("db.bulk.count", rowsProcessed);
			activity.SetTag("db.bulk.rows_affected", rowsProcessed);
			activity.SetStatus(ActivityStatusCode.Ok);

			_logger.BulkInsertCompleted(rowsProcessed, tableName, stopwatch.ElapsedMilliseconds);

			return rowsProcessed;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			activity.SetStatus(ActivityStatusCode.Error);
			activity.SetTag("exception.type", ex.GetType().FullName);
			activity.AddEvent("exception", DateTimeOffset.UtcNow);

			_logger.BulkInsertFailed(ex, tableName, stopwatch.ElapsedMilliseconds);

			throw;
		}
	}

	/// <inheritdoc />
	public async Task<long> BulkInsertAsync(
		DbContext context,
		IDataReader reader,
		BulkInsertOptions options,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(reader);
		ArgumentNullException.ThrowIfNull(options);
		ValidateOptions(options);

		if (string.IsNullOrEmpty(options.TableName))
		{
			throw new InvalidOperationException("TableName is required when using IDataReader overload.");
		}

		var tableName = NormalizeDestinationTableName(options.TableName);

		using var activity = _tracing.StartActivity("BulkInsert", ActivityKind.Client);
		activity.SetTag("db.system", "mssql");
		activity.SetTag("db.operation", "BulkInsert");
		activity.SetTag("db.sql.table", tableName);

		_logger.BulkInsertStarted(tableName);
		var stopwatch = Stopwatch.StartNew();

		try
		{
			var connection = context.Database.GetDbConnection() as SqlConnection
				?? throw new InvalidOperationException("BulkInsert requires a SqlConnection. Ensure you're using SQL Server provider.");

			var transaction = context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;

			var wasConnectionClosed = connection.State == ConnectionState.Closed;
			if (wasConnectionClosed)
			{
				await connection.OpenAsync(cancellationToken);
			}

			try
			{
				var rowsCopied = await ExecuteBulkCopyFromReaderAsync(
					connection,
					transaction,
					reader,
					tableName,
					options,
					cancellationToken);

				stopwatch.Stop();

				activity.SetTag("db.bulk.rows_affected", rowsCopied);
				activity.SetStatus(ActivityStatusCode.Ok);

				_logger.BulkInsertCompleted(rowsCopied, tableName, stopwatch.ElapsedMilliseconds);

				return rowsCopied;
			}
			finally
			{
				if (wasConnectionClosed && connection.State == ConnectionState.Open)
				{
					await connection.CloseAsync();
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			activity.SetStatus(ActivityStatusCode.Error);
			activity.SetTag("exception.type", ex.GetType().FullName);
			activity.AddEvent("exception", DateTimeOffset.UtcNow);

			_logger.BulkInsertFailed(ex, tableName, stopwatch.ElapsedMilliseconds);

			throw;
		}
	}

	private async Task<long> ExecuteBulkCopyFromReaderAsync(
		SqlConnection connection,
		SqlTransaction? transaction,
		IDataReader reader,
		string tableName,
		BulkInsertOptions options,
		CancellationToken cancellationToken)
	{
		await using var ownedTransaction = transaction is null
			? (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken)
			: null;
		var effectiveTransaction = transaction ?? ownedTransaction;
		var bulkCopyOptions = BuildSqlBulkCopyOptions(options);

		using var bulkCopy = new SqlBulkCopy(connection, bulkCopyOptions, effectiveTransaction)
		{
			DestinationTableName = tableName,
			BatchSize = options.BatchSize,
			BulkCopyTimeout = options.Timeout,
			EnableStreaming = true
		};

		// Map columns from reader schema if not explicitly provided
		var columns = options.Columns;
		if (columns is null || columns.Length == 0)
		{
			var schemaTable = reader.GetSchemaTable();
			if (schemaTable is not null)
			{
				columns = schemaTable.Rows
					.Cast<DataRow>()
					.Select(r => r[SchemaTableColumn.ColumnName]?.ToString() ?? string.Empty)
					.Where(name => !string.IsNullOrEmpty(name))
					.ToArray();
			}
		}

		if (columns is not null)
		{
			foreach (var column in columns)
			{
				bulkCopy.ColumnMappings.Add(column, column);
			}
		}

		// Set up progress notification with row counting
		if (options.NotifyAfter is not null)
		{
			bulkCopy.NotifyAfter = options.NotifyAfterRows ?? options.BatchSize;
			bulkCopy.SqlRowsCopied += (_, args) =>
			{
				options.NotifyAfter(args.RowsCopied);
			};
		}

		try
		{
			await bulkCopy.WriteToServerAsync(reader, cancellationToken);

			if (ownedTransaction is not null)
			{
				await ownedTransaction.CommitAsync(cancellationToken);
			}

			// Prefer reader's RecordsAffected for exact count (if the reader tracks it).
			// SqlBulkCopy.RowsCopied only reflects the last NotifyAfter boundary, missing trailing rows.
			return reader.RecordsAffected >= 0 ? reader.RecordsAffected : bulkCopy.RowsCopied;
		}
		catch
		{
			await RollbackOwnedTransactionAsync(ownedTransaction, tableName);
			throw;
		}
	}

	private async Task ExecuteBulkCopyAsync<T>(
		SqlConnection connection,
		SqlTransaction? transaction,
		IEnumerable<T> items,
		string tableName,
		string[] columns,
		BulkInsertOptions options,
		CancellationToken cancellationToken) where T : class
	{
		await using var ownedTransaction = transaction is null
			? (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken)
			: null;
		var effectiveTransaction = transaction ?? ownedTransaction;
		var bulkCopyOptions = BuildSqlBulkCopyOptions(options);

		using var bulkCopy = new SqlBulkCopy(connection, bulkCopyOptions, effectiveTransaction)
		{
			DestinationTableName = tableName,
			BatchSize = options.BatchSize,
			BulkCopyTimeout = options.Timeout,
			EnableStreaming = true
		};

		// Map columns — resolve [Column] attribute for DB destination name
		foreach (var column in columns)
		{
			var dbColumn = ResolveDbColumnName<T>(column);
			bulkCopy.ColumnMappings.Add(column, dbColumn);
		}

		// Set up progress notification
		if (options.NotifyAfter is not null)
		{
			// Use NotifyAfterRows if specified, otherwise default to BatchSize
			bulkCopy.NotifyAfter = options.NotifyAfterRows ?? options.BatchSize;
			bulkCopy.SqlRowsCopied += (_, args) =>
			{
				options.NotifyAfter(args.RowsCopied);
			};
		}

		// Use EnumerableDataReader for high-performance IDataReader.
		using var reader = new EnumerableDataReader<T>(items, columns);
		try
		{
			await bulkCopy.WriteToServerAsync(reader, cancellationToken);

			if (ownedTransaction is not null)
			{
				await ownedTransaction.CommitAsync(cancellationToken);
			}
		}
		catch
		{
			await RollbackOwnedTransactionAsync(ownedTransaction, tableName);
			throw;
		}
	}

	private static SqlBulkCopyOptions BuildSqlBulkCopyOptions(BulkInsertOptions options)
	{
		var result = SqlBulkCopyOptions.Default;

		if (options.CheckConstraints)
			result |= SqlBulkCopyOptions.CheckConstraints;

		if (options.FireTriggers)
			result |= SqlBulkCopyOptions.FireTriggers;

		if (options.KeepIdentity)
			result |= SqlBulkCopyOptions.KeepIdentity;

		if (options.KeepNulls)
			result |= SqlBulkCopyOptions.KeepNulls;

		if (options.TableLock)
			result |= SqlBulkCopyOptions.TableLock;

		return result;
	}

	private async Task RollbackOwnedTransactionAsync(SqlTransaction? transaction, string tableName)
	{
		if (transaction is null)
		{
			return;
		}

		try
		{
			await transaction.RollbackAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.BulkInsertRollbackFailed(ex, tableName);
		}
	}

	private static void ValidateOptions(BulkInsertOptions options)
	{
		Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);
	}

	internal static string NormalizeDestinationTableName(string tableName)
	{
		var parts = tableName.Split('.', StringSplitOptions.TrimEntries);
		if (parts.Length is < 1 or > 2 || parts.Any(static part => string.IsNullOrWhiteSpace(part)))
		{
			throw new ArgumentException("Destination table must be a table name or a schema-qualified table name.", nameof(tableName));
		}

		using var builder = new SqlCommandBuilder();
		return string.Join('.', parts.Select(part => builder.QuoteIdentifier(UnwrapIdentifier(part))));
	}

	private static string UnwrapIdentifier(string identifier)
	{
		if (identifier.Length > 1 && identifier[0] == '[' && identifier[^1] == ']')
		{
			return identifier[1..^1].Replace("]]", "]", StringComparison.Ordinal);
		}

		if (identifier.Contains('[', StringComparison.Ordinal) || identifier.Contains(']', StringComparison.Ordinal))
		{
			throw new ArgumentException("Destination table contains an invalid bracketed identifier.", nameof(identifier));
		}

		return identifier;
	}

	internal (string TableName, string[] Columns) GetEntityMetadata<T>(DbContext context)
	{
		var entityType = context.Model.FindEntityType(typeof(T));

		// Resolve table name first (needed for cache key)
		string tableName;
		if (entityType is null)
		{
			tableName = typeof(T).Name;
		}
		else
		{
			var schema = entityType.GetSchema();
			var table = entityType.GetTableName() ?? typeof(T).Name;
			tableName = string.IsNullOrEmpty(schema) ? table : $"{schema}.{table}";
		}

		// Cache columns by (EntityType, TableName) - handles multi-tenant schemas
		var cacheKey = (EntityType: typeof(T), TableName: tableName);

		var columns = _columnCache.GetOrAdd(cacheKey, _ =>
		{
			if (entityType is null)
			{
				// Fallback: use public properties
				return typeof(T).GetProperties()
					.Where(p => p.CanRead && p.CanWrite)
					.Select(p => p.Name)
					.ToArray();
			}

			// Get mapped column names for insert:
			// - Exclude identity columns (auto-increment)
			// - Exclude computed columns (OnAddOrUpdate) like RowVersion, temporal columns
			// - Include columns with defaults (OnAdd) - user can provide value, otherwise DB default is used
			return entityType.GetProperties()
				.Where(p => !p.IsShadowProperty())
				.Where(p => p.GetValueGenerationStrategy() != SqlServerValueGenerationStrategy.IdentityColumn)
				.Where(p => p.ValueGenerated != ValueGenerated.OnAddOrUpdate)
				.Select(p => p.GetColumnName() ?? p.Name)
				.ToArray();
		});

		return (tableName, columns);
	}

	/// <summary>
	/// Resolves the DB column name for a C# property.
	/// If the property has a [Column("Name")] attribute, returns that name.
	/// Otherwise returns the input name as-is (backward-compatible with DB column names).
	/// </summary>
	private static string ResolveDbColumnName<T>(string propertyName)
	{
		return ResolveDbColumnName(typeof(T), propertyName);
	}

	/// <inheritdoc cref="ResolveDbColumnName{T}(string)"/>
	internal static string ResolveDbColumnName(Type type, string propertyName)
	{
		return _dbColumnNameCache.GetOrAdd((type, propertyName), static key =>
		{
			var property = key.Item1.GetProperty(key.Item2, BindingFlags.Instance | BindingFlags.Public);
			return property?.GetCustomAttribute<ColumnAttribute>()?.Name ?? key.Item2;
		});
	}

}
