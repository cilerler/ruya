using System.ComponentModel.DataAnnotations;

namespace Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

/// <summary>
/// Configuration settings for bulk operations.
/// Bind from configuration section "BulkInsertOperations".
/// </summary>
public sealed class BulkInsertOperationsSettings
{
    public const string ConfigurationSectionName = nameof(BulkInsertOperations);

    /// <summary>
    /// Timeout in seconds. Default is 30.
    /// </summary>
    [Range(1, 86400)]
    public int Timeout { get; set; } = 30;

    /// <summary>
    /// Default batch size. Default is 1000.
    /// </summary>
    [Range(1, 1_000_000)]
    public int BatchSize { get; set; } = 1000;
}
