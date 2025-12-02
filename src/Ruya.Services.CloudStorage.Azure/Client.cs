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
    private readonly ILogger _logger;
    private readonly BlobServiceClient _serviceClient;

    // Cache to avoid calling CreateIfNotExistsAsync on every request.
    // Key: Bucket/Container Name, Value: True if exists (we only cache existence)
    private readonly ConcurrentDictionary<string, bool> _containerExistenceCache = new();

    private static readonly ActivitySource _activitySource = new("Ruya.Services.CloudStorage.Azure");
    private static readonly Meter _meter = new("Ruya.Services.CloudStorage.Azure");
    private static readonly Counter<long> _filesUploaded = _meter.CreateCounter<long>("files_uploaded");
    private static readonly Counter<long> _bytesUploaded = _meter.CreateCounter<long>("bytes_uploaded");
    private static readonly Counter<long> _filesDownloaded = _meter.CreateCounter<long>("files_downloaded");
    private static readonly Counter<long> _filesDeleted = _meter.CreateCounter<long>("files_deleted");
    private static readonly Counter<long> _filesFailed = _meter.CreateCounter<long>("files_failed");

    public Client(IConfiguration configuration, ILogger<Client> logger, IOptions<Setting> options)
        : this(logger, CreateBlobServiceClient(configuration, options.Value))
    {
    }

    public Client(ILogger<Client> logger, BlobServiceClient serviceClient)
    {
        _logger = logger;
        _serviceClient = serviceClient;
    }

    private static BlobServiceClient CreateBlobServiceClient(IConfiguration configuration, Setting settings)
    {
        var connectionString = configuration.GetConnectionString(settings.ConnectionStringKey)
            ?? throw new ArgumentNullException(nameof(Setting.ConnectionStringKey));
        return new BlobServiceClient(connectionString);
    }

    public async Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(GetFileMetadataAsync));
        activity?.SetTag("bucket", bucketName);
        activity?.SetTag("fileName", fileName);

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be null or whitespace.", nameof(fileName));

        var containerClient = await GetContainerClientAsync(bucketName, cancellationToken);
        var blobClient = containerClient.GetBlobClient(fileName);

        try
        {
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
             _logger.LogInformation("Not Found - {fileName} in container {containerName}", fileName, bucketName);
             throw new FileNotFoundException($"Not Found - {fileName} in container {bucketName}", fileName, ex);
        }
        catch (Exception ex)
        {
             _filesFailed.Add(1);
             _logger.LogError(ex, ex.Message);
             throw;
        }
    }

    public async Task<CloudFileMetadata> UploadFileAsync(string bucketName, string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(UploadFileAsync));
        activity?.SetTag("bucket", bucketName);
        activity?.SetTag("sourcePath", sourcePath);

        string contentType = "application/octet-stream";
        try
        {
            contentType = MimeTypesMap.GetMimeType(sourcePath);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "An error occured while trying to retrieve MimeType");
        }

        using FileStream fileStream = File.OpenRead(sourcePath);
        return await UploadStreamAsync(bucketName, fileStream, targetPath, contentType, cancellationToken);
    }

    public async Task<CloudFileMetadata> UploadStreamAsync(string bucketName, Stream sourceStream, string targetPath, string contentType, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(UploadStreamAsync));
        activity?.SetTag("bucket", bucketName);
        activity?.SetTag("targetPath", targetPath);

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var containerClient = await GetContainerClientAsync(bucketName, cancellationToken);

        string destinationFileName = PathNormalizer.ToCloudPath(targetPath);

        var blobClient = containerClient.GetBlobClient(destinationFileName);

        try
        {
             if (sourceStream.CanSeek)
             {
                 sourceStream.Seek(0, SeekOrigin.Begin);
             }

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                ProgressHandler = new Progress<long>(p => _logger.LogTrace("Uploading {file} to {container}: {bytes} bytes", destinationFileName, bucketName, p))
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
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(e, "Encountered an error while uploading file stream. {ContainerName} {FileName}", bucketName, destinationFileName);
             throw;
        }
    }

    public async Task DownloadFileAsync(string bucketName, string fileName, Stream targetStream, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(DownloadFileAsync));
        activity?.SetTag("bucket", bucketName);
        activity?.SetTag("fileName", fileName);

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));

        var containerClient = await GetContainerClientAsync(bucketName, cancellationToken);
        var blobClient = containerClient.GetBlobClient(fileName);

        try
        {
            await blobClient.DownloadToAsync(targetStream, cancellationToken);
            _filesDownloaded.Add(1);
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(e, "Encountered an error while dowloading file. {ContainerName} {FileName}", bucketName, fileName);
             throw;
        }

        if (targetStream.CanSeek) targetStream.Seek(0, SeekOrigin.Begin);
    }

    public async Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(DeleteFileAsync));
        activity?.SetTag("bucket", bucketName);
        activity?.SetTag("fileName", fileName);

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));

        var containerClient = await GetContainerClientAsync(bucketName, cancellationToken);
        var blobClient = containerClient.GetBlobClient(fileName);

        try
        {
            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            _filesDeleted.Add(1);
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(e, "Encountered an error while deleting file. {ContainerName} {FileName}", bucketName, fileName);
             throw;
        }
    }

    public async Task CopyFileAsync(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(CopyFileAsync));
        activity?.SetTag("sourceBucket", sourceBucketName);
        activity?.SetTag("destinationBucket", destinationBucketName);

        if (string.IsNullOrWhiteSpace(sourceBucketName)) throw new ArgumentException("Source bucket name cannot be null or whitespace.", nameof(sourceBucketName));
        if (string.IsNullOrWhiteSpace(destinationBucketName)) throw new ArgumentException("Destination bucket name cannot be null or whitespace.", nameof(destinationBucketName));

        var sourceContainer = await GetContainerClientAsync(sourceBucketName, cancellationToken);
        var sourceBlob = sourceContainer.GetBlobClient(sourceFileName);

        var destinationContainer = await GetContainerClientAsync(destinationBucketName, cancellationToken);
        var destinationBlob = destinationContainer.GetBlobClient(destinationFileName);

        try
        {
             if (!await sourceBlob.ExistsAsync(cancellationToken))
             {
                  throw new FileNotFoundException($"Source file {sourceFileName} in bucket {sourceBucketName} not found.");
             }

             var sourceUri = GetBlobSasUri(sourceBlob, BlobSasPermissions.Read, 60);

             var operation = await destinationBlob.StartCopyFromUriAsync(new Uri(sourceUri), cancellationToken: cancellationToken);
             await operation.WaitForCompletionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
             _filesFailed.Add(1);
             _logger.LogError(ex, "Error copying file");
             throw;
        }
    }

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(GetFileListAsync));
        activity?.SetTag("bucket", bucketName);

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        var containerClient = await GetContainerClientAsync(bucketName, cancellationToken);

        AsyncPageable<BlobItem> blobs;
        try
        {
             blobs = containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken);
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(e, "Encountered an error while getting file list. {Prefix} {ContainerName}", prefix, bucketName);
             throw;
        }

        await foreach (var blobItem in blobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new CloudFileMetadata(
                bucketName,
                blobItem.Name,
                (ulong?)blobItem.Properties.ContentLength,
                blobItem.Properties.LastModified?.UtcDateTime,
                blobItem.Properties.ContentType,
                GetBlobSasUri(containerClient.GetBlobClient(blobItem.Name), BlobSasPermissions.Read, 60)
            );
        }
    }

    public string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60)
    {
        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        var containerClient = _serviceClient.GetBlobContainerClient(bucketName);
        var blobClient = containerClient.GetBlobClient(filename);
        return GetBlobSasUri(blobClient, BlobSasPermissions.Write, expirationMinutes);
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(string bucketName, CancellationToken cancellationToken)
    {
        var containerClient = _serviceClient.GetBlobContainerClient(bucketName);

        // Optimization: Check cache first. If it's there, we assume it exists.
        if (_containerExistenceCache.ContainsKey(bucketName))
        {
            return containerClient;
        }

        // If not in cache, create if not exists and add to cache.
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        _containerExistenceCache.TryAdd(bucketName, true);
        
        return containerClient;
    }

    private static string GetBlobSasUri(BlobClient blobClient, BlobSasPermissions permissions, int expirationMinutes)
    {
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
}
