using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using HeyRed.Mime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.CloudStorage.Abstractions;
using static Ruya.Services.CloudStorage.Abstractions.PathNormalizer;

namespace Ruya.Services.CloudStorage.Azure;

public class Client : ICloudFileService
{
    private const string InstrumentationName =
        $"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.CloudStorage)}.{nameof(Ruya.Services.CloudStorage.Azure)}";

    private readonly ILogger _logger;
    private readonly BlobServiceClient _serviceClient;

    // Cache to avoid calling CreateIfNotExistsAsync on every request.
    // Key: Bucket/Container Name, Value: True if exists (we only cache existence)
    private readonly ConcurrentDictionary<string, bool> _containerExistenceCache = new();

    private static readonly ActivitySource _activitySource = new(InstrumentationName);
    private static readonly Meter _meter = new(InstrumentationName);
    private static readonly Counter<long> _filesUploaded = _meter.CreateCounter<long>("files_uploaded");
    private static readonly Counter<long> _bytesUploaded = _meter.CreateCounter<long>("bytes_uploaded");
    private static readonly Counter<long> _filesDownloaded = _meter.CreateCounter<long>("files_downloaded");
    private static readonly Counter<long> _filesDeleted = _meter.CreateCounter<long>("files_deleted");
    private static readonly Counter<long> _filesFailed = _meter.CreateCounter<long>("files_failed");

    public Client(IConfiguration configuration, ILogger<Client> logger, IOptions<Setting> options)
        : this(logger, CreateBlobServiceClient(configuration, GetSettings(options)))
    {
    }

    public Client(ILogger<Client> logger, BlobServiceClient serviceClient)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceClient);

        _logger = logger;
        _serviceClient = serviceClient;
    }

    private static BlobServiceClient CreateBlobServiceClient(IConfiguration configuration, Setting settings)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration.GetConnectionString(settings.ConnectionStringKey)
            ?? throw new InvalidOperationException(
                $"Connection string catalog entry '{settings.ConnectionStringKey}' is not configured.");
        return new BlobServiceClient(connectionString);
    }

    private static Setting GetSettings(IOptions<Setting> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Value;
    }

    public async Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(GetFileMetadataAsync));

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be null or whitespace.", nameof(fileName));

        try
        {
            var containerClient = await GetContainerClientAsync(bucketName, createIfMissing: false, cancellationToken);
            var blobClient = containerClient.GetBlobClient(fileName);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            return new CloudFileMetadata(
                bucketName,
                fileName,
                (ulong?)properties.Value.ContentLength,
                properties.Value.LastModified.UtcDateTime,
                properties.Value.ContentType,
                GetBlobSasUri(blobClient, BlobSasPermissions.Read, 60)
            );
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
             _filesFailed.Add(1);
             _logger.LogInformation(LogEvents.MetadataNotFound, "Azure cloud-storage object was not found");
             throw new FileNotFoundException($"Not Found - {fileName} in container {bucketName}", fileName, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             throw;
        }
        catch (Exception ex)
        {
             _filesFailed.Add(1);
             _logger.LogError(LogEvents.MetadataFailed, ex, "Azure cloud-storage metadata request failed");
             throw;
        }
    }

    public async Task<CloudFileMetadata> UploadFileAsync(string bucketName, string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(UploadFileAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        cancellationToken.ThrowIfCancellationRequested();

        string contentType = "application/octet-stream";
        try
        {
            contentType = MimeTypesMap.GetMimeType(sourcePath);
        }
        catch (Exception e)
        {
            _logger.LogWarning(LogEvents.MimeTypeFailed, e, "An error occured while trying to retrieve MimeType");
        }

        await using FileStream fileStream = OpenUploadSourceFile(sourcePath, cancellationToken);
        return await UploadStreamAsync(bucketName, fileStream, targetPath, contentType, cancellationToken);
    }

    public async Task<CloudFileMetadata> UploadStreamAsync(string bucketName, Stream sourceStream, string targetPath, string contentType, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(UploadStreamAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(sourceStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        string destinationFileName = PathNormalizer.ToCloudPath(targetPath);

        try
        {
             var containerClient = await GetContainerClientAsync(bucketName, createIfMissing: true, cancellationToken);
             var blobClient = containerClient.GetBlobClient(destinationFileName);

             if (sourceStream.CanSeek)
             {
                 sourceStream.Seek(0, SeekOrigin.Begin);
             }

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            };

            await blobClient.UploadAsync(sourceStream, uploadOptions, cancellationToken);

            if (sourceStream.CanSeek) _bytesUploaded.Add(sourceStream.Length);
            _filesUploaded.Add(1);

            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            return new CloudFileMetadata(
                bucketName,
                destinationFileName,
                (ulong?)properties.Value.ContentLength,
                properties.Value.LastModified.UtcDateTime,
                properties.Value.ContentType,
                GetBlobSasUri(blobClient, BlobSasPermissions.Read, 60)
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             throw;
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(LogEvents.UploadFailed, e, "Azure cloud-storage stream upload failed");
             throw;
        }
    }

    public async Task DownloadFileAsync(string bucketName, string fileName, Stream targetStream, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(DownloadFileAsync));

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be null or whitespace.", nameof(fileName));
        ArgumentNullException.ThrowIfNull(targetStream);

        try
        {
            var containerClient = await GetContainerClientAsync(bucketName, createIfMissing: false, cancellationToken);
            var blobClient = containerClient.GetBlobClient(fileName);
            await blobClient.DownloadToAsync(targetStream, cancellationToken);

            if (targetStream.CanSeek)
            {
                targetStream.Seek(0, SeekOrigin.Begin);
            }

            _filesDownloaded.Add(1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             throw;
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(LogEvents.DownloadFailed, e, "Azure cloud-storage download failed");
             throw;
        }

    }

    public async Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(DeleteFileAsync));

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be null or whitespace.", nameof(fileName));

        try
        {
            var containerClient = await GetContainerClientAsync(bucketName, createIfMissing: false, cancellationToken);
            var blobClient = containerClient.GetBlobClient(fileName);
            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            _filesDeleted.Add(1);
        }
        catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
        {
            // Delete is idempotent. Azure can report a missing container as a 404
            // before DeleteIfExistsAsync gets a chance to report a missing blob.
            _filesDeleted.Add(1);
            _logger.LogDebug(LogEvents.DeleteNotFound, "Azure container or blob was already absent while deleting a file.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             throw;
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(LogEvents.DeleteFailed, e, "Azure cloud-storage delete failed");
             throw;
        }
    }

    public async Task CopyFileAsync(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(CopyFileAsync));

        if (string.IsNullOrWhiteSpace(sourceBucketName)) throw new ArgumentException("Source bucket name cannot be null or whitespace.", nameof(sourceBucketName));
        if (string.IsNullOrWhiteSpace(sourceFileName)) throw new ArgumentException("Source file name cannot be null or whitespace.", nameof(sourceFileName));
        if (string.IsNullOrWhiteSpace(destinationBucketName)) throw new ArgumentException("Destination bucket name cannot be null or whitespace.", nameof(destinationBucketName));
        if (string.IsNullOrWhiteSpace(destinationFileName)) throw new ArgumentException("Destination file name cannot be null or whitespace.", nameof(destinationFileName));

        try
        {
             var sourceContainer = await GetContainerClientAsync(sourceBucketName, createIfMissing: false, cancellationToken);
             var sourceBlob = sourceContainer.GetBlobClient(sourceFileName);

             var destinationContainer = await GetContainerClientAsync(destinationBucketName, createIfMissing: true, cancellationToken);
             var destinationBlob = destinationContainer.GetBlobClient(destinationFileName);

             if (!await sourceBlob.ExistsAsync(cancellationToken))
             {
                  throw new FileNotFoundException($"Source file {sourceFileName} in bucket {sourceBucketName} not found.");
             }

             var sourceUri = GetBlobSasUri(sourceBlob, BlobSasPermissions.Read, 60);

             var operation = await destinationBlob.StartCopyFromUriAsync(new Uri(sourceUri), cancellationToken: cancellationToken);
             await operation.WaitForCompletionAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             throw;
        }
        catch (Exception ex)
        {
             _filesFailed.Add(1);
             _logger.LogError(LogEvents.CopyFailed, ex, "Error copying file");
             throw;
        }
    }

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(GetFileListAsync));

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        BlobContainerClient containerClient;
        AsyncPageable<BlobItem> blobs;
        try
        {
             containerClient = await GetContainerClientAsync(bucketName, createIfMissing: false, cancellationToken);
             blobs = containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             throw;
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(LogEvents.ListFailed, e, "Azure cloud-storage listing failed");
             throw;
        }

        IAsyncEnumerator<BlobItem> enumerator;
        try
        {
            enumerator = blobs.GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.ListFailed, exception, "Azure cloud-storage listing failed");
            throw;
        }

        await using (enumerator)
        {
            while (true)
            {
                CloudFileMetadata metadata;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    BlobItem blobItem = enumerator.Current;
                    metadata = new CloudFileMetadata(
                        bucketName,
                        blobItem.Name,
                        (ulong?)blobItem.Properties.ContentLength,
                        blobItem.Properties.LastModified?.UtcDateTime,
                        blobItem.Properties.ContentType,
                        GetBlobSasUri(containerClient.GetBlobClient(blobItem.Name), BlobSasPermissions.Read, 60)
                    );
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _filesFailed.Add(1);
                    _logger.LogError(LogEvents.ListFailed, exception, "Azure cloud-storage listing failed");
                    throw;
                }

                yield return metadata;
            }
        }
    }

    public string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60)
    {
        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("File name cannot be null or whitespace.", nameof(filename));
        if (string.IsNullOrWhiteSpace(contentType)) throw new ArgumentException("Content type cannot be null or whitespace.", nameof(contentType));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expirationMinutes);
        var containerClient = _serviceClient.GetBlobContainerClient(bucketName);
        var blobClient = containerClient.GetBlobClient(filename);
        return GetBlobSasUri(blobClient, BlobSasPermissions.Write, expirationMinutes);
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(
        string bucketName,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var containerClient = _serviceClient.GetBlobContainerClient(bucketName);

        if (!createIfMissing || _containerExistenceCache.ContainsKey(bucketName))
        {
            return containerClient;
        }

        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        _containerExistenceCache.TryAdd(bucketName, true);
        
        return containerClient;
    }

    private static string GetBlobSasUri(BlobClient blobClient, BlobSasPermissions permissions, int expirationMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expirationMinutes);
        if (!blobClient.CanGenerateSasUri) return string.Empty;

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes)
        };
        sasBuilder.SetPermissions(permissions);

        return blobClient.GenerateSasUri(sasBuilder).ToString();
    }

    private FileStream OpenUploadSourceFile(string sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            return new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.UploadFailed, exception, "Could not open the Azure cloud-storage upload source file");
            throw;
        }
    }
}
