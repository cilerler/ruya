using System;
using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.TokenBroker.Validation;

/// <summary>
/// Validates that the string is a valid Base64-encoded value with optional minimum decoded length.
/// </summary>
/// <remarks>
/// This custom attribute is used instead of <see cref="System.ComponentModel.DataAnnotations.Base64StringAttribute"/>
/// (added in .NET 8) because the built-in attribute only validates Base64 format. This attribute additionally
/// supports <see cref="MinimumDecodedLength"/> to enforce minimum key sizes (e.g., 32 bytes / 256 bits for HMAC-SHA256).
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class Base64StringAttribute : ValidationAttribute
{
    /// <summary>
    /// Gets or sets the minimum length of the decoded byte array.
    /// Default is 0 (no minimum).
    /// </summary>
    public int MinimumDecodedLength { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Base64StringAttribute"/> class.
    /// </summary>
    public Base64StringAttribute()
        : base("The field {0} must be a valid Base64 string.")
    {
    }

    /// <summary>
    /// Determines whether the specified value is a valid Base64 string.
    /// </summary>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Null values are handled by [Required] attribute
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is not string base64String)
        {
            return new ValidationResult($"The field {validationContext.DisplayName} must be a string.");
        }

        // Empty strings treated as null (handled by [Required])
        if (string.IsNullOrWhiteSpace(base64String))
        {
            return ValidationResult.Success;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64String);

            if (MinimumDecodedLength > 0 && bytes.Length < MinimumDecodedLength)
            {
                return new ValidationResult(
                    $"The field {validationContext.DisplayName} must decode to at least {MinimumDecodedLength} bytes. Actual: {bytes.Length} bytes.");
            }

            return ValidationResult.Success;
        }
        catch (FormatException)
        {
            return new ValidationResult(
                $"The field {validationContext.DisplayName} must be a valid Base64 string.");
        }
    }
}
