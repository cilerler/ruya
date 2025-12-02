using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.UnitTests;

/// <summary>
/// A fake implementation of ICloudFileService for testing purposes.
/// </summary>
public class FakeCloudFileService : ICloudFileService
{
    public Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
        => Task.FromResult(new CloudFileMetadata(bucketName, fileName, 100, DateTime.UtcNow, "text/plain", "http://fake.url"));

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new CloudFileMetadata(bucketName, "file1.txt", 100, DateTime.UtcNow, "text/plain", "http://fake.url");
    }

    public Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task CopyFileAsync(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DownloadFileAsync(string bucketName, string fileName, Stream targetStream, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<CloudFileMetadata> UploadFileAsync(string bucketName, string sourcePath, string targetPath, CancellationToken cancellationToken = default)
        => Task.FromResult(new CloudFileMetadata(bucketName, targetPath, 100, DateTime.UtcNow, "text/plain", "http://fake.url"));

    public Task<CloudFileMetadata> UploadStreamAsync(string bucketName, Stream sourceStream, string targetPath, string contentType, CancellationToken cancellationToken = default)
        => Task.FromResult(new CloudFileMetadata(bucketName, targetPath, 100, DateTime.UtcNow, contentType, "http://fake.url"));

    public string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60)
        => $"http://fake.url/{bucketName}/{filename}";
}

/// <summary>
/// Another fake for testing multiple provider registration.
/// </summary>
public class AnotherFakeCloudFileService : FakeCloudFileService
{
}
