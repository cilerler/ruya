using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

namespace Ruya.EntityFrameworkCore.SqlServer;

/// <summary>
/// Extension methods for DbContext to enable bulk operations.
/// </summary>
public static class DbContextExtensions
{
    /// <summary>
    /// Bulk inserts entities using SqlBulkCopy. Auto-detects table and columns from DbContext model.
    /// </summary>
    /// <typeparam name="TEntity">Entity type registered in DbContext.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows inserted.</returns>
    /// <remarks>
    /// Requires IBulkInsertOperations to be registered in DI.
    /// Uses the current transaction if one exists on the DbContext.
    /// </remarks>
    public static async Task<long> BulkInsertAsync<TEntity>(
        this DbContext context,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);

        var bulkOperations = GetBulkOperations(context);
        return await bulkOperations.BulkInsertAsync(context, entities, cancellationToken);
    }

    /// <summary>
    /// Bulk inserts entities using SqlBulkCopy with explicit table name and columns.
    /// </summary>
    /// <typeparam name="T">Type of entities.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="tableName">Destination table name.</param>
    /// <param name="columns">Column names to map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows inserted.</returns>
    public static async Task<long> BulkInsertAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        string tableName,
        string[] columns,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        var bulkOperations = GetBulkOperations(context);
        return await bulkOperations.BulkInsertAsync(context, entities, tableName, columns, cancellationToken);
    }

    /// <summary>
    /// Bulk inserts entities using SqlBulkCopy with full configuration options.
    /// </summary>
    /// <typeparam name="T">Type of entities.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="options">Bulk insert configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows inserted.</returns>
    public static async Task<long> BulkInsertAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        BulkInsertOptions options,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        var bulkOperations = GetBulkOperations(context);
        return await bulkOperations.BulkInsertAsync(context, entities, options, cancellationToken);
    }

    /// <summary>
    /// Bulk inserts entities using SqlBulkCopy with inline options configuration.
    /// </summary>
    /// <typeparam name="T">Type of entities.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows inserted.</returns>
    public static async Task<long> BulkInsertAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        Action<BulkInsertOptions> configureOptions,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var options = new BulkInsertOptions();
        configureOptions(options);

        var bulkOperations = GetBulkOperations(context);
        return await bulkOperations.BulkInsertAsync(context, entities, options, cancellationToken);
    }

    private static IBulkInsertOperations GetBulkOperations(DbContext context)
    {
        var coreOptionsExtension = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>();

        var applicationServiceProvider = coreOptionsExtension?.ApplicationServiceProvider
            ?? throw new InvalidOperationException(
                "ApplicationServiceProvider is not configured. Call UseApplicationServiceProvider(sp) on DbContextOptions.");

        return applicationServiceProvider.GetService<IBulkInsertOperations>()
            ?? throw new InvalidOperationException(
                "IBulkInsertOperations is not registered. Call services.AddBulkInsertOperations() in your DI configuration.");
    }
}
