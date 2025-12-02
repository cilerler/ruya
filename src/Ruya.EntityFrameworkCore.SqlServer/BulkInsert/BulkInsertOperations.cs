using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
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

		using var activity = _tracing.StartActivity("BulkInsert", ActivityKind.Client);
		activity.SetTag("db.system", "mssql");
		activity.SetTag("db.operation", "BulkInsert");
		activity.SetTag("db.sql.table", options.TableName);

		// Optimization: If we can cheaply determine empty, do it. But don't iterate.
		if (entities.TryGetNonEnumeratedCount(out var count))
		{
			if (count == 0)
			{
				_logger.LogDebug("BulkInsert skipped: no entities provided");
				return 0;
			}
			activity.SetTag("db.bulk.count", count);
		}

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

		if (columns is null || columns.Length == 0)
		{
			throw new InvalidOperationException($"Could not determine columns for type {typeof(T).Name}. Provide Columns in options.");
		}

		// Wrap entities to count them as we stream
		var counter = new Counter();
		var wrappedEntities = CountItems(entities, counter);

		_logger.LogDebug("BulkInsert starting to {TableName}", tableName);
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
				await ExecuteBulkCopyAsync(
					connection,
					transaction,
					wrappedEntities,
					tableName,
					columns,
					options,
					cancellationToken);

				stopwatch.Stop();

				var rowsProcessed = counter.Value;

				// Update trace with actual count
				activity.SetTag("db.bulk.count", rowsProcessed);
				activity.SetTag("db.bulk.rows_affected", rowsProcessed); // SqlBulkCopy doesn't return rows affected accurately without event, but assuming success means all processed.
				activity.SetStatus(ActivityStatusCode.Ok);

				_logger.LogInformation(
					"BulkInsert completed: {RowsCopied} rows to {TableName} in {ElapsedMs}ms",
					rowsProcessed, tableName, stopwatch.ElapsedMilliseconds);

				return rowsProcessed;
			}
			finally
			{
				if (wasConnectionClosed && connection.State == ConnectionState.Open)
				{
					await connection.CloseAsync();
				}
			}
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.SetTag("exception.type", ex.GetType().FullName);
                activity.SetTag("exception.message", ex.Message);
                activity.SetTag("exception.stacktrace", ex.StackTrace);
                activity.AddEvent("exception", DateTimeOffset.UtcNow);

			_logger.LogError(ex,
				"BulkInsert failed: {TableName} after {ElapsedMs}ms - {Message}",
				tableName, stopwatch.ElapsedMilliseconds, ex.Message);

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
		var bulkCopyOptions = BuildSqlBulkCopyOptions(options);

		using var bulkCopy = new SqlBulkCopy(connection, bulkCopyOptions, transaction)
		{
			DestinationTableName = tableName,
			BatchSize = options.BatchSize,
			BulkCopyTimeout = options.Timeout,
			EnableStreaming = true
		};

		// Map columns
		foreach (var column in columns)
		{
			bulkCopy.ColumnMappings.Add(column, column);
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
		await bulkCopy.WriteToServerAsync(reader, cancellationToken);
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

	private class Counter { public long Value; }

	private static IEnumerable<T> CountItems<T>(IEnumerable<T> source, Counter counter)
	{
		foreach (var item in source)
		{
			counter.Value++;
			yield return item;
		}
	}
}
