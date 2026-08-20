using System;
using Microsoft.Extensions.Logging;

namespace Ruya.EntityFrameworkCore.SqlServer;

internal static partial class Log
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Building SelectForUpdate query for {SchemaName}.{TableName} with BatchSize={BatchSize}, LockedBy={LockedBy}, called by {CallerMethod}")]
    internal static partial void BatchLockQueryBuilding(this ILogger logger, string schemaName, string tableName, int? batchSize, string lockedBy, string callerMethod);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Building SelectForUpdateKeys query for {SchemaName}.{TableName} with BatchSize={BatchSize}, LockedBy={LockedBy}, called by {CallerMethod}")]
    internal static partial void BatchLockKeysQueryBuilding(this ILogger logger, string schemaName, string tableName, int? batchSize, string lockedBy, string callerMethod);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug, Message = "BulkInsert skipped: no entities provided")]
    internal static partial void BulkInsertSkipped(this ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug, Message = "BulkInsert starting to {TableName}")]
    internal static partial void BulkInsertStarted(this ILogger logger, string tableName);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "BulkInsert completed: {RowsCopied} rows to {TableName} in {ElapsedMs}ms")]
    internal static partial void BulkInsertCompleted(this ILogger logger, long rowsCopied, string tableName, long elapsedMs);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Error, Message = "BulkInsert failed for {TableName} after {ElapsedMs}ms")]
    internal static partial void BulkInsertFailed(this ILogger logger, Exception exception, string tableName, long elapsedMs);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Error, Message = "BulkInsert rollback failed for {TableName}")]
    internal static partial void BulkInsertRollbackFailed(this ILogger logger, Exception exception, string tableName);
}
