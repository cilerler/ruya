using System;
using System.ComponentModel.DataAnnotations;

namespace Ruya.EntityFrameworkCore.SqlServer.BatchLock;

/// <summary>
/// Options for batch lock operations using SELECT FOR UPDATE pattern.
/// </summary>
public sealed class BatchLockOptions
{
	/// <summary>
	/// Schema name of the target table.
	/// </summary>
	[Required]
	public string SchemaName { get; init; } = "dbo";

	/// <summary>
	/// Name of the target table.
	/// </summary>
	[Required]
	public string TableName { get; init; } = null!;

	/// <summary>
	/// Number of rows to lock in a single batch. Defaults to 1.
	/// </summary>
	[Range(1, 10000)]
	public int? BatchSize { get; init; }

	/// <summary>
	/// Identifier of the process locking the rows. Defaults to hostname.
	/// </summary>
	[Required]
	public string LockedBy { get; init; } = System.Net.Dns.GetHostName();

	/// <summary>
	/// Lock state value to set on locked rows. Defaults to 1.
	/// </summary>
	public byte LockState { get; init; } = 1;

	/// <summary>
	/// Lock timestamp. Defaults to UTC now if not specified.
	/// </summary>
	public DateTime? LockTime { get; init; }

	/// <summary>
	/// Comma-separated list of column names to exclude from the result set.
	/// </summary>
	public string? ExcludeFields { get; init; }

	/// <summary>
	/// Custom WHERE clause. If null, default conditions are applied based on table schema.
	/// WARNING: Ensure this value is validated to prevent SQL injection.
	/// </summary>
	public string? WhereClause { get; init; }

	/// <summary>
	/// Custom ORDER BY clause. If null, ProcessingOrder column is used if it exists.
	/// WARNING: Ensure this value is validated to prevent SQL injection.
	/// </summary>
	public string? OrderByClause { get; init; }

	/// <summary>
	/// Name of the process status code field. Defaults to "ProcessStatusCode".
	/// </summary>
	public string? ProcessStatusCodeField { get; init; }

	/// <summary>
	/// Value of process status code to filter by.
	/// </summary>
	public byte? ProcessStatusCodeValue { get; init; }

	/// <summary>
	/// Name of the processing order field. Defaults to "ProcessingOrder".
	/// </summary>
	public string? ProcessingOrderField { get; init; }

	/// <summary>
	/// Name of the primary key field. Defaults to "Id".
	/// </summary>
	public string? PrimaryKeyField { get; init; }

	/// <summary>
	/// When true, only returns the primary key column instead of all columns.
	/// Useful for reducing data transfer when you only need the IDs.
	/// </summary>
	public bool ReturnPrimaryKeyOnly { get; init; }
}
