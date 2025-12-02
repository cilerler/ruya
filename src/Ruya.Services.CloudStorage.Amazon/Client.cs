using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using HeyRed.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.CloudStorage.Abstractions;
using static Ruya.Services.CloudStorage.Abstractions.PathNormalizer;

using System.Runtime.CompilerServices;

namespace Ruya.Services.CloudStorage.Amazon;

public class Client : ICloudFileService
{
    private readonly ILogger _logger;
    private readonly Setting _options;
    private readonly IAmazonS3 _s3Client;

    private static readonly ActivitySource _activitySource = new("Ruya.Services.CloudStorage.Amazon");
    private static readonly Meter _meter = new("Ruya.Services.CloudStorage.Amazon");
    private static readonly Counter<long> _filesUploaded = _meter.CreateCounter<long>("files_uploaded");
    private static readonly Counter<long> _bytesUploaded = _meter.CreateCounter<long>("bytes_uploaded");
    private static readonly Counter<long> _filesDownloaded = _meter.CreateCounter<long>("files_downloaded");
    private static readonly Counter<long> _filesDeleted = _meter.CreateCounter<long>("files_deleted");
    private static readonly Counter<long> _filesFailed = _meter.CreateCounter<long>("files_failed");

    public Client(ILogger<Client> logger, IOptions<Setting> options)
        : this(logger, options, CreateS3Client(options.Value))
    {
    }

    public Client(ILogger<Client> logger, IOptions<Setting> options, IAmazonS3 s3Client)
    {
        _logger = logger;
        _options = options.Value;
        _s3Client = s3Client;
    }

    private static IAmazonS3 CreateS3Client(Setting settings)
    {
        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(settings.Region))
        {
            config.RegionEndpoint = global::Amazon.RegionEndpoint.GetBySystemName(settings.Region);
        }
        if (!string.IsNullOrWhiteSpace(settings.ServiceUrl))
        {
            config.ServiceURL = settings.ServiceUrl;
            config.ForcePathStyle = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.AccessKey) && !string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            return new AmazonS3Client(settings.AccessKey, settings.SecretKey, config);
        }
        else
        {
            return new AmazonS3Client(config);
        }
    }

    public async Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(GetFileMetadataAsync));
        activity?.SetTag("bucket", bucketName);
        activity?.SetTag("fileName", fileName);

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be null or whitespace.", nameof(fileName));

        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = fileName
            };
            var response = await _s3Client.GetObjectMetadataAsync(request, cancellationToken);

            return new CloudFileMetadata(
                bucketName,
                fileName,
                (ulong)response.ContentLength,
                response.LastModified.GetValueOrDefault().ToUniversalTime(),
                response.Headers.ContentType,
                GetPreSignedUrl(bucketName, fileName, 60)
            );
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _filesFailed.Add(1);
            _logger.LogInformation("Not Found - {fileName} in bucket {bucketName}", fileName, bucketName);
            throw new FileNotFoundException($"Not Found - {fileName} in bucket {bucketName}", fileName, ex);
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

        string destinationFileName = PathNormalizer.ToCloudPath(targetPath);

        if (sourceStream.CanSeek)
        {
             sourceStream.Seek(0, SeekOrigin.Begin);
        }

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = destinationFileName,
            InputStream = sourceStream,
            ContentType = contentType,
            AutoCloseStream = false
        };

        request.StreamTransferProgress += (sender, args) => {
            _logger.LogTrace("Uploading {file} to {bucket}: {bytes} bytes ({percent}%)", destinationFileName, bucketName, args.TransferredBytes, args.PercentDone);
        };

        try
        {
            await _s3Client.PutObjectAsync(request, cancellationToken);

            if (sourceStream.CanSeek) _bytesUploaded.Add(sourceStream.Length);
            _filesUploaded.Add(1);

            return await GetFileMetadataAsync(bucketName, destinationFileName, cancellationToken);
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(e, "Error uploading file");
             throw;
        }
    }

    public async Task DownloadFileAsync(string bucketName, string fileName, Stream targetStream, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(DownloadFileAsync));
        activity?.SetTag("bucket", bucketName);
        activity?.SetTag("fileName", fileName);

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));

        try
        {
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = fileName
            };
            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            await response.ResponseStream.CopyToAsync(targetStream, cancellationToken);
            _filesDownloaded.Add(1);
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(e, "Error downloading file");
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

        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = fileName
            };
            await _s3Client.DeleteObjectAsync(request, cancellationToken);
            _filesDeleted.Add(1);
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(e, "Error deleting file");
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

        try
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = sourceBucketName,
                SourceKey = sourceFileName,
                DestinationBucket = destinationBucketName,
                DestinationKey = destinationFileName
            };
            await _s3Client.CopyObjectAsync(request, cancellationToken);
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(e, "Error copying file");
            throw;
        }
    }

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(GetFileListAsync));
        activity?.SetTag("bucket", bucketName);

        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));

        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = prefix
        };

        ListObjectsV2Response response;
        do
        {
            try
            {
                response = await _s3Client.ListObjectsV2Async(request, cancellationToken);
            }
            catch (Exception e)
            {
                _filesFailed.Add(1);
                _logger.LogError(e, "Error getting file list");
                throw;
            }

            foreach (var obj in response.S3Objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new CloudFileMetadata(
                    bucketName,
                    obj.Key,
                    (ulong)obj.Size,
                    obj.LastModified.GetValueOrDefault().ToUniversalTime(),
                    string.Empty, // ListObjects doesn't return content type usually
                    GetPreSignedUrl(bucketName, obj.Key, 60)
                );
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
    }

    public string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60)
    {
        if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = filename,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            ContentType = contentType
        };
        return _s3Client.GetPreSignedURL(request);
    }

    private string GetPreSignedUrl(string bucketName, string key, int expirationMinutes)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes)
        };
        return _s3Client.GetPreSignedURL(request);
    }
}
