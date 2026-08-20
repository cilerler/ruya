using Microsoft.Extensions.Logging;

namespace Ruya.Services.CloudStorage.Google;

internal static class LogEvents
{
    internal static readonly EventId MetadataNotFound = new(8300, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(MetadataNotFound)}");
    internal static readonly EventId MetadataFailed = new(8301, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(MetadataFailed)}");
    internal static readonly EventId MimeTypeFailed = new(8302, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(MimeTypeFailed)}");
    internal static readonly EventId UploadFailed = new(8303, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(UploadFailed)}");
    internal static readonly EventId DownloadFailed = new(8304, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(DownloadFailed)}");
    internal static readonly EventId DeleteNotFound = new(8305, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(DeleteNotFound)}");
    internal static readonly EventId DeleteFailed = new(8306, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(DeleteFailed)}");
    internal static readonly EventId CopyFailed = new(8307, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(CopyFailed)}");
    internal static readonly EventId ListFailed = new(8308, $"{nameof(Ruya.Services.CloudStorage.Google)}{nameof(ListFailed)}");
}
