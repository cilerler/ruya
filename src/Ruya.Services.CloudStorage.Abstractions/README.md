# Ruya.Services.CloudStorage.Abstractions

Core abstractions for the Ruya Cloud Storage framework. It defines a unified, asynchronous, and stateless interface for managing files across various storage providers (AWS S3, Azure Blob Storage, Google Cloud Storage, Local File System).

## Features

-   **Unified Interface**: `ICloudFileService` abstracts away provider differences.
-   **Stateless Design**: Bucket/Container names are passed per operation, allowing a single instance to manage multiple containers.
-   **Factory Support**: `ICloudStorageFactory` enables using multiple providers in the same application.
-   **Async I/O**: Streaming operations use asynchronous I/O and accept cancellation tokens.
-   **Observability**: Built-in OpenTelemetry Tracing (`ActivitySource`) and Metrics (`Meter`).

## Usage

### Core Interface

The `ICloudFileService` interface provides standard file operations:

```csharp
public interface ICloudFileService
{
    Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default);
    IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default);
    Task CopyFileAsync(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string bucketName, string fileName, Stream targetStream, CancellationToken cancellationToken = default);
    Task<CloudFileMetadata> UploadFileAsync(string bucketName, string sourcePath, string targetPath, CancellationToken cancellationToken = default);
    Task<CloudFileMetadata> UploadStreamAsync(string bucketName, Stream sourceStream, string targetPath, string contentType, CancellationToken cancellationToken = default);
    string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60);
}
```

### Object-key semantics

`targetPath` on the two upload methods is the only object-key input normalized by the remote providers. Both local path separators are accepted there, and the returned `CloudFileMetadata.Name` is the canonical key to use for later operations.

All other key-shaped inputs are opaque, exact provider keys: `fileName`, `prefix`, both copy file names, and the `filename` used for a signed upload URL. They are deliberately not normalized because a backslash is a valid literal character in S3, Azure Blob Storage, and Google Cloud Storage object names. Pass the canonical `CloudFileMetadata.Name` back rather than reconstructing or renormalizing it. The Local provider maps both separator characters to its host file system and returns canonical names with `/`.

### Using the Factory

Inject `ICloudStorageFactory` to access specific providers by name:

```csharp
public class StorageService
{
    private readonly ICloudFileService _aws;
    private readonly ICloudFileService _azure;

    public StorageService(ICloudStorageFactory factory)
    {
        _aws = factory.GetService("Amazon");
        _azure = factory.GetService("Azure");
    }
}
```

## Observability

The libraries emit traces and metrics using `System.Diagnostics`.

-   **Metrics**:
    -   `files_uploaded` (Counter)
    -   `bytes_uploaded` (Counter)
    -   `files_downloaded` (Counter)
    -   `files_deleted` (Counter)
    -   `files_failed` (Counter)
