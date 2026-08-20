using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

using Microsoft.IdentityModel.Tokens;

namespace Ruya.Services.TokenBroker.Validation;

internal static class RsaPublicKeyFactory
{
    public static IReadOnlyDictionary<string, SecurityKey> Create(
        IReadOnlyDictionary<string, string> configuredKeys)
    {
        ArgumentNullException.ThrowIfNull(configuredKeys);

        return configuredKeys.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pair.Value);
                return (SecurityKey)new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false))
                {
                    KeyId = pair.Key
                };
            },
            StringComparer.Ordinal);
    }

    public static bool PrivateKeyMatchesPublicKey(string privateKeyPem, string publicKeyPem)
    {
        try
        {
            using var privateRsa = RSA.Create();
            privateRsa.ImportFromPem(privateKeyPem);
            using var publicRsa = RSA.Create();
            publicRsa.ImportFromPem(publicKeyPem);

            var privateParameters = privateRsa.ExportParameters(includePrivateParameters: false);
            var publicParameters = publicRsa.ExportParameters(includePrivateParameters: false);
            return FixedTimeEquals(privateParameters.Modulus, publicParameters.Modulus)
                && FixedTimeEquals(privateParameters.Exponent, publicParameters.Exponent);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(byte[]? left, byte[]? right) =>
        left is not null
        && right is not null
        && left.Length == right.Length
        && CryptographicOperations.FixedTimeEquals(left, right);
}
