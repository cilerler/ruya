using Microsoft.Extensions.Logging;

namespace Ruya.Services.DistributedLock.MsSql;

internal static class LogEvents
{
    internal static readonly EventId AcquireRejected = new(8600, "SqlLockAcquireRejected");
    internal static readonly EventId Acquired = new(8601, "SqlLockAcquired");
    internal static readonly EventId AcquireFailed = new(8602, "SqlLockAcquireFailed");
    internal static readonly EventId OwnershipVerificationFailed = new(8603, "SqlLockOwnershipVerificationFailed");
    internal static readonly EventId Extended = new(8604, "SqlLockExtended");
    internal static readonly EventId ReleaseFailed = new(8605, "SqlLockReleaseFailed");
    internal static readonly EventId Released = new(8606, "SqlLockReleased");
    internal static readonly EventId AutoReleaseStarted = new(8607, "SqlLockAutoReleaseStarted");
    internal static readonly EventId AutoReleaseFailed = new(8608, "SqlLockAutoReleaseFailed");
    internal static readonly EventId SessionCloseFailed = new(8609, "SqlLockSessionCloseFailed");
    internal static readonly EventId SessionDisposeFailed = new(8610, "SqlLockSessionDisposeFailed");
    internal static readonly EventId ProviderDisposeFailed = new(8611, "SqlLockProviderDisposeFailed");
}
