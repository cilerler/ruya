using System;
using System.Linq;
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

		_logger.LogDebug(
			"Building SelectForUpdate query for {SchemaName}.{TableName} with BatchSize={BatchSize}, LockedBy={LockedBy},  called by {CallerMethod}",
			options.SchemaName,
			options.TableName,
			options.BatchSize,
			options.LockedBy,
			callerMethod);

		var parameters = BuildParameters(options);
		return _dbContext.Set<T>().FromSqlRaw(SqlQuery.SelectForUpdate, parameters);
	}

	private static SqlParameter[] BuildParameters(BatchLockOptions options)
	{
		return
		[
				new("@p0", options.SchemaName),
				new("@p1", options.TableName),
				new("@p2", (object)options.BatchSize ?? DBNull.Value),
				new("@p3", (object)options.LockedBy ?? DBNull.Value),
				new("@p4", (object)options.LockState ?? DBNull.Value),
				new("@p5", (object)options.LockTime ?? DBNull.Value),
				new("@p6", (object)options.ExcludeFields ?? DBNull.Value),
				new("@p7", (object)options.WhereClause ?? DBNull.Value),
				new("@p8", (object)options.OrderByClause ?? DBNull.Value),
				new("@p9", (object)options.ProcessStatusCodeField ?? DBNull.Value),
				new("@p10", (object)options.ProcessStatusCodeValue ?? DBNull.Value),
				new("@p11", (object)options.ProcessingOrderField ?? DBNull.Value),
				//new SqlParameter("@p12", (object)options.Debug ?? DBNull.Value),
				new("@p12", DBNull.Value)
		];
	}
}
