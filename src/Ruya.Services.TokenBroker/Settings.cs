using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using Ruya.Services.TokenBroker.Validation;

namespace Ruya.Services.TokenBroker;

public class TokenBrokerSettings : IValidatableObject
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
    [MinLength(1)]
    public string SigningKeyId { get; set; } = string.Empty;

    [Required]
    [RsaPrivateKeyPem]
    public string SigningPrivateKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// Public verification keys accepted by broker validation and exchange operations.
    /// Keep the previous public key here until every token it signed has expired.
    /// </summary>
    [RsaPublicKeyPemCollection]
    public Dictionary<string, string> SigningPublicKeys { get; } = new(StringComparer.Ordinal);

    [Obsolete("Symmetric signing is no longer supported. Configure SigningKeyId and SigningPrivateKeyPem.")]
    public string? SigningKeyBase64 { get; set; }

    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan ApiKeyCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
#pragma warning disable CS0618 // The compatibility member must be inspected so legacy configuration fails closed.
        if (!string.IsNullOrWhiteSpace(SigningKeyBase64))
        {
            yield return new ValidationResult(
                "TokenBroker:SigningKeyBase64 is no longer accepted. Remove it and configure the RSA private-key settings.",
                [nameof(SigningKeyBase64)]);
        }
#pragma warning restore CS0618

        if (!string.IsNullOrWhiteSpace(SigningKeyId)
            && SigningPublicKeys.TryGetValue(SigningKeyId, out var currentPublicKey)
            && !string.IsNullOrWhiteSpace(SigningPrivateKeyPem)
            && !RsaPublicKeyFactory.PrivateKeyMatchesPublicKey(SigningPrivateKeyPem, currentPublicKey))
        {
            yield return new ValidationResult(
                "TokenBroker:SigningPublicKeys must map the current SigningKeyId to the public half of SigningPrivateKeyPem.",
                [nameof(SigningPublicKeys), nameof(SigningPrivateKeyPem), nameof(SigningKeyId)]);
        }

        if (!string.IsNullOrWhiteSpace(SigningKeyId) && !SigningPublicKeys.ContainsKey(SigningKeyId))
        {
            yield return new ValidationResult(
                "TokenBroker:SigningPublicKeys must contain the current SigningKeyId.",
                [nameof(SigningPublicKeys), nameof(SigningKeyId)]);
        }
    }
}
