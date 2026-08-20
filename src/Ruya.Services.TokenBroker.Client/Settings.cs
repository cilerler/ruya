using System;
using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.TokenBroker.Client;

public class TokenClientSettings
{
    public const string ConfigurationSectionName = nameof(TokenClient);

    [Required]
    [Url]
    public required string TokenBrokerUrl { get; set; }

    [Required]
    [MinLength(1)]
    public required string ServiceName { get; set; }

    [Required]
    [MinLength(16)]
    public required string ApiKey { get; set; }

    /// <summary>
    /// Allows clear-text HTTP only for explicitly configured development environments.
    /// Loopback URLs are accepted without this switch.
    /// </summary>
    public bool AllowInsecureHttpForDevelopment { get; set; }

    [Range(typeof(TimeSpan), "00:00:30", "00:10:00")]
    public TimeSpan TokenRefreshBuffer { get; set; } = TimeSpan.FromMinutes(1);
}
