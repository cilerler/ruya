using System.Collections.Generic;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis.Contracts;

/// <summary>
/// Provides data protection services for encrypting and decrypting content.
/// </summary>
public interface IDataProtection
{
    /// <summary>
    /// Protects (encrypts) the specified content.
    /// </summary>
    /// <param name="content">The content to protect.</param>
    /// <param name="purposes">Optional purpose strings for creating a purpose-specific protector.</param>
    /// <returns>The protected content as a base64-encoded string.</returns>
    string Protect(string content, IEnumerable<string>? purposes = null);

    /// <summary>
    /// Unprotects (decrypts) the specified content.
    /// </summary>
    /// <param name="content">The protected content to unprotect.</param>
    /// <param name="purposes">Optional purpose strings that must match the purposes used during protection.</param>
    /// <returns>The original unprotected content.</returns>
    string Unprotect(string content, IEnumerable<string>? purposes = null);
}
