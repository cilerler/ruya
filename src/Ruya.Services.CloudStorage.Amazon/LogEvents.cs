using Microsoft.Extensions.Logging;

namespace Ruya.Services.CloudStorage.Amazon;

internal static class LogEvents
{
    internal static readonly EventId MetadataNotFound = new(8100, $"{nameof(Ruya.Services.CloudStorage.Amazon)}{nameof(MetadataNotFound)}");
    internal static readonly EventId MetadataFailed = new(8101, $"{nameof(Ruya.Services.CloudStorage.Amazon)}{nameof(MetadataFailed)}");
    internal static readonly EventId MimeTypeFailed = new(8102, $"{nameof(Ruya.Services.CloudStorage.Amazon)}{nameof(MimeTypeFailed)}");
    internal static readonly EventId UploadFailed = new(8103, $"{nameof(Ruya.Services.CloudStorage.Amazon)}{nameof(UploadFailed)}");
    internal static readonly EventId DownloadFailed = new(8104, $"{nameof(Ruya.Services.CloudStorage.Amazon)}{nameof(DownloadFailed)}");
    internal static readonly EventId DeleteFailed = new(8105, $"{nameof(Ruya.Services.CloudStorage.Amazon)}{nameof(DeleteFailed)}");
    internal static readonly EventId CopyFailed = new(8106, $"{nameof(Ruya.Services.CloudStorage.Amazon)}{nameof(CopyFailed)}");
    internal static readonly EventId ListFailed = new(8107, $"{nameof(Ruya.Services.CloudStorage.Amazon)}{nameof(ListFailed)}");
}
