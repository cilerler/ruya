using System;
using System.ComponentModel.DataAnnotations;

namespace Ruya.EntityFrameworkCore.SqlServer.BatchLock;

/// <summary>
/// Options for batch lock operations using SELECT FOR UPDATE pattern.
/// </summary>
public sealed class BatchLockOptions
{
	/// <summary>
	/// Schema name of the target table. Defaults to "dbo".
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
	/// If true, the ProcessStatusCodeField will be updated with the value of ProcessStatusCodeNextValue when acquiring the lock.
	/// This permits explicit nullification by supplying a null ProcessStatusCodeNextValue value.
	/// </summary>
	public bool UpdateProcessStatusCode { get; init; }

	/// <summary>
	/// The next process status code value to set on the locked rows if UpdateProcessStatusCode is true.
	/// </summary>
	public byte? ProcessStatusCodeNextValue { get; init; }

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
	/// This is set internally by the method being called (SelectForUpdate vs SelectForUpdateKeysAsync).
	/// </summary>
	internal bool ReturnPrimaryKeyOnly { get; set; }

	/// <summary>
	/// When true, sets ModifiedAt = ModifiedAt instead of SYSUTCDATETIME().
	/// This prevents the table trigger from updating the timestamp.
	/// </summary>
	public bool PreserveModifiedAt { get; init; }

	/// <summary>
	/// When true, omits ModifiedAt from the SET clause entirely.
	/// This allows table triggers that check IF NOT UPDATE(ModifiedAt) to fire.
	/// </summary>
	public bool OmitModifiedAt { get; init; }
}
