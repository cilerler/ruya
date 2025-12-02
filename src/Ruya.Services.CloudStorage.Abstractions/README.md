# Ruya.Services.CloudStorage.Abstractions

Core abstractions for the Ruya Cloud Storage framework. It defines a unified, asynchronous, and stateless interface for managing files across various storage providers (AWS S3, Azure Blob Storage, Google Cloud Storage, Local File System).

## Features

-   **Unified Interface**: `ICloudFileService` abstracts away provider differences.
-   **Stateless Design**: Bucket/Container names are passed per operation, allowing a single instance to manage multiple containers.
-   **Factory Support**: `ICloudStorageFactory` enables using multiple providers in the same application.
-   **Async I/O**: Fully asynchronous API for high performance.
-   **Observability**: Built-in OpenTelemetry Tracing (`ActivitySource`) and Metrics (`Meter`).

## Usage

### Core Interface

The `ICloudFileService` interface provides standard file operations:

```csharp
public interface ICloudFileService
{
    Task UploadFileAsync(string containerName, string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    Task UploadStreamAsync(string containerName, Stream stream, string destinationPath, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadFileAsync(string containerName, string path, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string containerName, string path, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string containerName, string path, CancellationToken cancellationToken = default);
    Task<CloudFileMetadata> GetMetadataAsync(string containerName, string path, CancellationToken cancellationToken = default);
}
```

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
