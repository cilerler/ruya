namespace Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

/// <summary>
/// Configuration settings for bulk operations.
/// Bind from configuration section "BulkInsertOperations".
/// </summary>
public sealed class BulkInsertOperationsSettings
{
    public const string ConfigurationSectionName = "BulkInsertOperations";

    /// <summary>
    /// Timeout in seconds. Default is 30.
    /// </summary>
    public int Timeout { get; set; } = 30;

    /// <summary>
    /// Default batch size. Default is 1000.
    /// </summary>
    public int BatchSize { get; set; } = 1000;
}
