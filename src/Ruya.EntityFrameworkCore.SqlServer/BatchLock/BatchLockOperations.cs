using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ruya.EntityFrameworkCore.SqlServer.BatchLock;

/// <summary>
/// Provides batch locking operations using SELECT FOR UPDATE pattern with ROWLOCK, UPDLOCK, READPAST hints.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public sealed class BatchLockOperations<TContext> : IBatchLockOperations where TContext : DbContext
{
	private readonly ILogger<BatchLockOperations<TContext>> _logger;
	private readonly TContext _dbContext;

	public BatchLockOperations(
		ILogger<BatchLockOperations<TContext>> logger,
		TContext dbContext)
	{
		_logger = logger;
		_dbContext = dbContext;
	}

	/// <inheritdoc />
	public IQueryable<T> SelectForUpdate<T>(BatchLockOptions options, [System.Runtime.CompilerServices.CallerMemberName] string callerMethod = "") where T : class
	{
		ArgumentNullException.ThrowIfNull(options);
		ValidateOptions(options);

		_logger.BatchLockQueryBuilding(
			options.SchemaName,
			options.TableName,
			options.BatchSize,
			options.LockedBy,
			callerMethod);

		var parameters = BuildParameters(options, returnPrimaryKeyOnly: false);
		return _dbContext.Set<T>().FromSqlRaw(SqlQuery.SelectForUpdate, parameters);
	}

	/// <inheritdoc />
	public async Task<List<TKey>> SelectForUpdateKeysAsync<TKey>(BatchLockOptions options, CancellationToken cancellationToken = default, [System.Runtime.CompilerServices.CallerMemberName] string callerMethod = "")
	{
		ArgumentNullException.ThrowIfNull(options);
		ValidateOptions(options);

		_logger.BatchLockKeysQueryBuilding(
			options.SchemaName,
			options.TableName,
			options.BatchSize,
			options.LockedBy,
			callerMethod);

		var parameters = BuildParameters(options, returnPrimaryKeyOnly: true);
		return await _dbContext.Database
			.SqlQueryRaw<TKey>(SqlQuery.SelectForUpdate, parameters)
			.ToListAsync(cancellationToken);
	}

	private static void ValidateOptions(BatchLockOptions options)
	{
		Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);
		ValidateOptionalIdentifier(options.ProcessStatusCodeField, nameof(options.ProcessStatusCodeField));
		ValidateOptionalIdentifier(options.ProcessingOrderField, nameof(options.ProcessingOrderField));
		ValidateOptionalIdentifier(options.PrimaryKeyField, nameof(options.PrimaryKeyField));
		ValidateExcludedFields(options.ExcludeFields);
	}

	private static void ValidateOptionalIdentifier(string? identifier, string propertyName)
	{
		if (identifier is not null && string.IsNullOrWhiteSpace(identifier))
		{
			throw new ValidationException($"{propertyName} must be null or a nonblank SQL identifier.");
		}
	}

	private static void ValidateExcludedFields(string? excludedFields)
	{
		if (excludedFields is null)
		{
			return;
		}

		foreach (var field in excludedFields.Split(','))
		{
			var trimmedField = field.Trim();
			if (trimmedField.Length is 0 or > 128)
			{
				throw new ValidationException(
					$"{nameof(BatchLockOptions.ExcludeFields)} must contain nonblank SQL identifiers no longer than 128 characters.");
			}
		}
	}

	private static SqlParameter[] BuildParameters(BatchLockOptions options, bool returnPrimaryKeyOnly)
	{
		return
		[
				new("@p0", options.SchemaName),
				new("@p1", options.TableName),
				new("@p2", (object?)options.BatchSize ?? DBNull.Value),
				new("@p3", options.LockedBy),
				new("@p4", options.LockState),
				new("@p5", (object?)options.LockTime ?? DBNull.Value),
				new("@p6", (object?)options.ExcludeFields ?? DBNull.Value),
				new("@p7", (object?)options.WhereClause ?? DBNull.Value),
				new("@p8", (object?)options.OrderByClause ?? DBNull.Value),
				new("@p9", (object?)options.ProcessStatusCodeField ?? DBNull.Value),
				new("@p10", (object?)options.ProcessStatusCodeValue ?? DBNull.Value),
				new("@p11", (object?)options.ProcessingOrderField ?? DBNull.Value),
				new("@p12", options.Debug),
				new("@p13", (object?)options.PrimaryKeyField ?? DBNull.Value),
				new("@p14", returnPrimaryKeyOnly),
				new("@p15", options.PreserveModifiedAt),
				new("@p16", options.OmitModifiedAt),
				new("@p17", options.UpdateProcessStatusCode),
				new("@p18", (object?)options.ProcessStatusCodeNextValue ?? DBNull.Value)
		];
	}
}
