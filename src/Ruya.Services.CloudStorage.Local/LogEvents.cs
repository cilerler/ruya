using Microsoft.Extensions.Logging;

namespace Ruya.Services.CloudStorage.Local;

internal static class LogEvents
{
    internal static readonly EventId MetadataNotFound = new(8400, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(MetadataNotFound)}");
    internal static readonly EventId MimeTypeFailed = new(8401, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(MimeTypeFailed)}");
    internal static readonly EventId UploadFailed = new(8402, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(UploadFailed)}");
    internal static readonly EventId UploadCleanupFailed = new(8403, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(UploadCleanupFailed)}");
    internal static readonly EventId DownloadFailed = new(8404, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(DownloadFailed)}");
    internal static readonly EventId DeleteNotFound = new(8405, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(DeleteNotFound)}");
    internal static readonly EventId DeleteFailed = new(8406, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(DeleteFailed)}");
    internal static readonly EventId CopyFailed = new(8407, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(CopyFailed)}");
    internal static readonly EventId CopyCleanupFailed = new(8408, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(CopyCleanupFailed)}");
    internal static readonly EventId ListFailed = new(8409, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(ListFailed)}");
    internal static readonly EventId MetadataProjectionFailed = new(8410, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(MetadataProjectionFailed)}");
    internal static readonly EventId EmptyDirectoryRemoved = new(8411, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(EmptyDirectoryRemoved)}");
    internal static readonly EventId EmptyDirectoryRemovalFailed = new(8412, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(EmptyDirectoryRemovalFailed)}");
    internal static readonly EventId MetadataFailed = new(8413, $"{nameof(Ruya.Services.CloudStorage.Local)}{nameof(MetadataFailed)}");
}
