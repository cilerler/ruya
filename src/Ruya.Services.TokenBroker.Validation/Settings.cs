using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Ruya.Services.TokenBroker.Validation;

namespace Ruya.Services.TokenBroker;

/// <summary>
/// Settings for token validation. This is a subset of TokenBrokerSettings
/// containing only what's needed for validation.
/// </summary>
public class TokenValidationSettings : IValidatableObject
{
    public const string ConfigurationSectionName = nameof(Ruya.Services.TokenBroker);

    [Required]
    public required string Issuer { get; set; }

    [Required]
    [MinLength(1)]
    public System.Collections.ObjectModel.Collection<string> Audiences { get; } = [];

    [RsaPublicKeyPemCollection]
    public Dictionary<string, string> SigningPublicKeys { get; } = new(StringComparer.Ordinal);

    [Obsolete("Symmetric validation is no longer supported. Configure SigningPublicKeys by key id.")]
    public string? SigningKeyBase64 { get; set; }

    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
#pragma warning disable CS0618 // The compatibility member must be inspected so legacy configuration fails closed.
        if (!string.IsNullOrWhiteSpace(SigningKeyBase64))
        {
            yield return new ValidationResult(
                "TokenBroker:SigningKeyBase64 is no longer accepted by validators. Remove it and configure public RSA keys only.",
                [nameof(SigningKeyBase64)]);
        }
#pragma warning restore CS0618
    }
}
