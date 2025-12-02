using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.DistributedLock.MsSql.Configuration;

/// <summary>
/// Configuration settings for SQL Server-based distributed locks.
/// </summary>
public sealed class SqlServerLockSettings
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string ConfigurationSectionName = "DistributedLock:SqlServer";

    /// <summary>
    /// Gets or sets the SQL Server connection string.
    /// This is populated internally from ConnectionStrings configuration using the ConnectionStringKey.
    /// </summary>
    [Required]
    public string ConnectionString { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection string key name used to retrieve the connection string from ConnectionStrings configuration section.
    /// </summary>
    [Required]
    public string ConnectionStringKey { get; set; } = string.Empty;
}
