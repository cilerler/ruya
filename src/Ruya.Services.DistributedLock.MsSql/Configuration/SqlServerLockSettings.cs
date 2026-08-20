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
    public const string ConfigurationSectionName = $"{nameof(Ruya.Services.DistributedLock)}:SqlServer";

    /// <summary>
    /// Gets the legacy SQL Server connection-string value.
    /// The key-based registration intentionally leaves this value unpopulated so credentials
    /// are resolved only at the provider use site. The member remains for 8.x API compatibility.
    /// </summary>
    public string ConnectionString { get; internal set; } = null!;

    /// <summary>
    /// Gets or sets the connection string key name used to retrieve the connection string from ConnectionStrings configuration section.
    /// </summary>
    [Required]
    public string ConnectionStringKey { get; set; } = null!;
}
