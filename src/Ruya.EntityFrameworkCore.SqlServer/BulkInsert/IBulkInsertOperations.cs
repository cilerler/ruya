using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

/// <summary>
/// Provides high-performance bulk insert operations for scenarios where EF Core's native methods are insufficient.
/// Use this only for bulk inserts; prefer EF Core's ExecuteUpdate/ExecuteDelete for other bulk operations.
/// </summary>
/// <remarks>
/// This service is registered as a singleton. Per-operation configuration should be done via <see cref="BulkInsertOptions"/>.
/// The <see cref="Timeout"/> and <see cref="BatchSize"/> properties are read-only defaults configured at startup.
/// </remarks>
public interface IBulkInsertOperations
{
    /// <summary>
    /// Default timeout in seconds for bulk operations.
    /// This is a read-only default; use <see cref="BulkInsertOptions.Timeout"/> for per-call configuration.
    /// </summary>
    int Timeout { get; }

    /// <summary>
    /// Default batch size for bulk copy operations.
    /// This is a read-only default; use <see cref="BulkInsertOptions.BatchSize"/> for per-call configuration.
    /// </summary>
    int BatchSize { get; }

    /// <summary>
    /// Bulk inserts entities using SqlBulkCopy. Auto-detects table name and columns from DbContext.
    /// </summary>
    /// <typeparam name="TEntity">Entity type registered in DbContext.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows inserted.</returns>
    Task<long> BulkInsertAsync<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    /// Bulk inserts entities using SqlBulkCopy with explicit table name and columns.
    /// </summary>
    /// <typeparam name="T">Type of entities.</typeparam>
    /// <param name="context">The DbContext instance (for connection/transaction sharing).</param>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="tableName">Destination table name.</param>
    /// <param name="columns">Column names to map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows inserted.</returns>
    Task<long> BulkInsertAsync<T>(
        DbContext context,
        IEnumerable<T> entities,
        string tableName,
        string[] columns,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Bulk inserts entities using SqlBulkCopy with full configuration options.
    /// </summary>
    /// <typeparam name="T">Type of entities.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="options">Bulk insert configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows inserted.</returns>
    Task<long> BulkInsertAsync<T>(
        DbContext context,
        IEnumerable<T> entities,
        BulkInsertOptions options,
        CancellationToken cancellationToken = default) where T : class;
}

/// <summary>
/// Configuration options for bulk insert operations.
/// </summary>
public sealed class BulkInsertOptions
{
    /// <summary>
    /// Destination table name. If null, auto-detected from DbContext.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Column names to map. If null, auto-detected from DbContext or type properties.
    /// </summary>
    public string[]? Columns { get; set; }

    /// <summary>
    /// Batch size for bulk copy. Default is 1000.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Timeout in seconds. Default is 30.
    /// </summary>
    public int Timeout { get; set; } = 30;

    /// <summary>
    /// Enable CHECK constraints during bulk copy. Default is true.
    /// </summary>
    public bool CheckConstraints { get; set; } = true;

    /// <summary>
    /// Fire triggers during bulk copy. Default is true.
    /// </summary>
    public bool FireTriggers { get; set; } = true;

    /// <summary>
    /// Keep identity values from source data. Default is false.
    /// </summary>
    public bool KeepIdentity { get; set; }

    /// <summary>
    /// Keep null values instead of using default values. Default is false.
    /// </summary>
    public bool KeepNulls { get; set; }

    /// <summary>
    /// Use table lock during bulk copy for better performance. Default is false.
    /// </summary>
    public bool TableLock { get; set; }

    /// <summary>
    /// Number of rows after which to invoke the progress callback.
    /// If null, defaults to <see cref="BatchSize"/> when <see cref="NotifyAfter"/> is set.
    /// This allows finer-grained progress updates without changing the write batch size.
    /// </summary>
    public int? NotifyAfterRows { get; set; }

    /// <summary>
    /// Progress callback invoked after every <see cref="NotifyAfterRows"/> rows are copied.
    /// </summary>
    public Action<long>? NotifyAfter { get; set; }
}
