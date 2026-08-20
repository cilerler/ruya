using Microsoft.Extensions.Logging;

namespace Ruya.Services.CloudStorage.Azure;

internal static class LogEvents
{
    internal static readonly EventId MetadataNotFound = new(8200, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(MetadataNotFound)}");
    internal static readonly EventId MetadataFailed = new(8201, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(MetadataFailed)}");
    internal static readonly EventId MimeTypeFailed = new(8202, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(MimeTypeFailed)}");
    internal static readonly EventId UploadFailed = new(8203, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(UploadFailed)}");
    internal static readonly EventId DownloadFailed = new(8204, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(DownloadFailed)}");
    internal static readonly EventId DeleteNotFound = new(8205, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(DeleteNotFound)}");
    internal static readonly EventId DeleteFailed = new(8206, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(DeleteFailed)}");
    internal static readonly EventId CopyFailed = new(8207, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(CopyFailed)}");
    internal static readonly EventId ListFailed = new(8208, $"{nameof(Ruya.Services.CloudStorage.Azure)}{nameof(ListFailed)}");
}
