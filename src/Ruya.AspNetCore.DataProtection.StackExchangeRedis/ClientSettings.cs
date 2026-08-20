using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

/// <summary>
/// Configuration settings for the data protection client.
/// </summary>
public sealed class DataProtectionClientSettings
{
    /// <summary>
    /// Configuration section name for binding.
    /// </summary>
    public const string ConfigurationSectionName = nameof(DataProtectionClientSettings);

    /// <summary>
    /// The named HTTP client used to retrieve remote data protection settings.
    /// </summary>
    public const string HttpClientName = "DataProtectionClient";

    /// <summary>
    /// The resolved remote configuration service address.
    /// </summary>
    [JsonIgnore]
    public string ConnectionString { get; internal set; } = null!;

    /// <summary>
    /// The connection string key name in the ConnectionStrings configuration section.
    /// </summary>
    [Required]
    public required string ConnectionStringKey { get; set; }

    /// <summary>
    /// The endpoint path to fetch data protection settings from.
    /// </summary>
    [Required]
    public required string Endpoint { get; set; }
}
