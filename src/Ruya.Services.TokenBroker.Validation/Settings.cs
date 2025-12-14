using System;
using System.ComponentModel.DataAnnotations;
using Ruya.Services.TokenBroker.Validation;

namespace Ruya.Services.TokenBroker;

/// <summary>
/// Settings for token validation. This is a subset of TokenBrokerSettings
/// containing only what's needed for validation.
/// </summary>
public class TokenValidationSettings
{
    public const string ConfigurationSectionName = "TokenBroker";

    [Required]
    public required string Issuer { get; set; }

    [Required]
    [MinLength(1)]
    public System.Collections.ObjectModel.Collection<string> Audiences { get; } = [];

    [Required]
    [Validation.Base64String(MinimumDecodedLength = 32)] // 256 bits minimum for HMAC-SHA256
    public required string SigningKeyBase64 { get; set; }

    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}
