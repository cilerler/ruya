using System;
using System.ComponentModel.DataAnnotations;

using Ruya.Services.TokenBroker.Validation;

namespace Ruya.Services.TokenBroker;

public class TokenBrokerSettings
{
    public const string ConfigurationSectionName = nameof(TokenBroker);

    /// <summary>
    /// Maximum allowed token lifetime in minutes that clients can request.
    /// Requests exceeding this value will be rejected.
    /// </summary>
    public const int MaxAllowedLifetimeMinutes = 1440; // 24 hours

    [Required]
    public required string Issuer { get; set; }

    [Required]
    [MinLength(1)]
    public System.Collections.ObjectModel.Collection<string> Audiences { get; init; } = [];

    [Required]
    [Ruya.Services.TokenBroker.Validation.Base64String(MinimumDecodedLength = 32)] // 256 bits minimum for HMAC-SHA256
    public required string SigningKeyBase64 { get; set; }

    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan ApiKeyCacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}
