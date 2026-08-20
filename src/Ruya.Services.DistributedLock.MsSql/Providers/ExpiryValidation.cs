using System;

namespace Ruya.Services.DistributedLock.MsSql.Providers;

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
