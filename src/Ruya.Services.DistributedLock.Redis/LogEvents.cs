using Microsoft.Extensions.Logging;

namespace Ruya.Services.DistributedLock.Redis;

internal static class LogEvents
{
    internal static readonly EventId CancellationCleanupFailed = new(8500, $"{nameof(Ruya.Services.DistributedLock.Redis)}{nameof(CancellationCleanupFailed)}");
    internal static readonly EventId AcquireFailed = new(8501, $"{nameof(Ruya.Services.DistributedLock.Redis)}{nameof(AcquireFailed)}");
    internal static readonly EventId ExtendFailed = new(8502, $"{nameof(Ruya.Services.DistributedLock.Redis)}{nameof(ExtendFailed)}");
    internal static readonly EventId ReleaseFailed = new(8503, $"{nameof(Ruya.Services.DistributedLock.Redis)}{nameof(ReleaseFailed)}");
    internal static readonly EventId ExistsFailed = new(8504, $"{nameof(Ruya.Services.DistributedLock.Redis)}{nameof(ExistsFailed)}");
    internal static readonly EventId ForceReleaseFailed = new(8505, $"{nameof(Ruya.Services.DistributedLock.Redis)}{nameof(ForceReleaseFailed)}");
    internal static readonly EventId NodeAcquireFailed = new(8510, "RedlockNodeAcquireFailed");
    internal static readonly EventId NodeExtendFailed = new(8511, "RedlockNodeExtendFailed");
    internal static readonly EventId NodeReleaseFailed = new(8512, "RedlockNodeReleaseFailed");
    internal static readonly EventId NodeExistsFailed = new(8513, "RedlockNodeExistsFailed");
    internal static readonly EventId NodeForceReleaseFailed = new(8514, "RedlockNodeForceReleaseFailed");
    internal static readonly EventId OwnedConnectionDisposeFailed = new(8515, "RedlockOwnedConnectionDisposeFailed");
}
