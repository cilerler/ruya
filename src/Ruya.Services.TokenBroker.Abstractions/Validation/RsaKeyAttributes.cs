using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace Ruya.Services.TokenBroker.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RsaPrivateKeyPemAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string pem || string.IsNullOrWhiteSpace(pem))
        {
            return ValidationResult.Success;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048)
            {
                return new ValidationResult("The RSA private key must be at least 2048 bits.");
            }

            return rsa.ExportParameters(includePrivateParameters: true).D is { Length: > 0 }
                ? ValidationResult.Success
                : new ValidationResult("The value must contain an RSA private key in PEM format.");
        }
        catch (CryptographicException)
        {
            return new ValidationResult("The value must contain an RSA private key in PEM format.");
        }
        catch (ArgumentException)
        {
            return new ValidationResult("The value must contain an RSA private key in PEM format.");
        }
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RsaPublicKeyPemCollectionAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not IReadOnlyDictionary<string, string> keys || keys.Count == 0)
        {
            return new ValidationResult("At least one keyed RSA public key is required.");
        }

        foreach (var (keyId, pem) in keys)
        {
            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(pem))
            {
                return new ValidationResult("Every RSA public key must have a nonblank key id and PEM value.");
            }

            if (pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                return new ValidationResult(
                    $"The validator key registered as '{keyId}' contains private key material. Configure a public key only.");
            }

            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                if (rsa.KeySize < 2048)
                {
                    return new ValidationResult(
                        $"The public key registered as '{keyId}' must be at least 2048 bits.");
                }
                _ = rsa.ExportParameters(includePrivateParameters: false);
            }
            catch (CryptographicException)
            {
                return new ValidationResult($"The public key registered as '{keyId}' is not a valid RSA PEM key.");
            }
            catch (ArgumentException)
            {
                return new ValidationResult($"The public key registered as '{keyId}' is not a valid RSA PEM key.");
            }
        }

        return ValidationResult.Success;
    }
}
