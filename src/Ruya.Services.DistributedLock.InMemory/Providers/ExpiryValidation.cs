using System;

namespace Ruya.Services.DistributedLock.InMemory.Providers;

internal static class ExpiryValidation
{
    internal static void Validate(TimeSpan expiry, string paramName = "expiry")
    {
        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, expiry, "Lock expiry must be greater than zero.");
        }
    }
}
