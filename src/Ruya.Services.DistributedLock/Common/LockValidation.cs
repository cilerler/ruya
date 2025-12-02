using System;

namespace Ruya.Services.DistributedLock.Common;

/// <summary>
/// Provides validation for lock keys and values.
/// </summary>
public static class LockValidation
{
    /// <summary>
    /// Maximum length for lock keys.
    /// Set to 255 characters to ensure cross-provider compatibility.
    /// This limit is based on SQL Server's sp_getapplock constraint, but is applied
    /// consistently across all providers to ensure portable code and predictable behavior.
    /// </summary>
    /// <remarks>
    /// While some providers (Redis, InMemory) could theoretically support longer keys,
    /// we use the most restrictive common denominator to prevent runtime errors when
    /// switching between provider implementations.
    /// </remarks>
    public const int MaxLockKeyLength = 255;

    /// <summary>
    /// Maximum length for lock values.
    /// </summary>
    public const int MaxLockValueLength = 256;

    /// <summary>
    /// Validates a lock key.
    /// </summary>
    /// <param name="lockKey">The lock key to validate.</param>
    /// <param name="paramName">The parameter name for exceptions.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public static void ValidateLockKey(string lockKey, string paramName = "lockKey")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey, paramName);

        if (lockKey.Length > MaxLockKeyLength)
        {
            throw new ArgumentException(
                $"Lock key exceeds maximum length of {MaxLockKeyLength} characters. Actual length: {lockKey.Length}",
                paramName);
        }
    }

    /// <summary>
    /// Validates a lock value.
    /// </summary>
    /// <param name="lockValue">The lock value to validate.</param>
    /// <param name="paramName">The parameter name for exceptions.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public static void ValidateLockValue(string lockValue, string paramName = "lockValue")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockValue, paramName);

        if (lockValue.Length > MaxLockValueLength)
        {
            throw new ArgumentException(
                $"Lock value exceeds maximum length of {MaxLockValueLength} characters. Actual length: {lockValue.Length}",
                paramName);
        }
    }
}
