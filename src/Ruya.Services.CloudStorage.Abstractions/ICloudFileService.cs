using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.CloudStorage.Abstractions;

public interface ICloudFileService
{
    /// <summary>
    ///     Gets the file's metadata, including a presigned url for downloading
    /// </summary>
    Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists all the files
    /// </summary>
    IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a remote file.
    /// </summary>
    /// <param name="bucketName">The name of the bucket/container.</param>
    /// <param name="fileName">The name/path of the file to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This operation is idempotent - if the file does not exist, the operation completes successfully
    /// without throwing an exception. This behavior is consistent across all cloud providers.
    /// </remarks>
    Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Copies given remote file to the provided bucket
    /// </summary>
    Task CopyFileAsync(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Downloads remote file to the provided target stream
    /// </summary>
    Task DownloadFileAsync(string bucketName, string fileName, Stream targetStream, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Generic method for uploading file to remote storage.
    /// </summary>
    Task<CloudFileMetadata> UploadFileAsync(string bucketName, string sourcePath, string targetPath, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Generic method for uploading stream to remote storage.
    /// </summary>
    Task<CloudFileMetadata> UploadStreamAsync(string bucketName, Stream sourceStream, string targetPath, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Generates a signed upload URL to upload files without authentication
    /// </summary>
    string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60);
}
