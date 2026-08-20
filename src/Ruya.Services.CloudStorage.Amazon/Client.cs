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

public class Client : ICloudFileService, IDisposable
{
    private const string InstrumentationName =
        $"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.CloudStorage)}.{nameof(Ruya.Services.CloudStorage.Amazon)}";

    private readonly ILogger _logger;
    private readonly Setting _options;
    private readonly IAmazonS3 _s3Client;
    private readonly bool _ownsS3Client;
    private int _disposeState;

    private static readonly ActivitySource _activitySource = new(InstrumentationName);
    private static readonly Meter _meter = new(InstrumentationName);
    private static readonly Counter<long> _filesUploaded = _meter.CreateCounter<long>("files_uploaded");
    private static readonly Counter<long> _bytesUploaded = _meter.CreateCounter<long>("bytes_uploaded");
    private static readonly Counter<long> _filesDownloaded = _meter.CreateCounter<long>("files_downloaded");
    private static readonly Counter<long> _filesDeleted = _meter.CreateCounter<long>("files_deleted");
    private static readonly Counter<long> _filesFailed = _meter.CreateCounter<long>("files_failed");

    public Client(ILogger<Client> logger, IOptions<Setting> options)
        : this(logger, options, CreateS3Client(GetSettings(options)), ownsS3Client: true)
    {
    }

    public Client(ILogger<Client> logger, IOptions<Setting> options, IAmazonS3 s3Client)
        : this(logger, options, s3Client, ownsS3Client: false)
    {
    }

    private Client(ILogger<Client> logger, IOptions<Setting> options, IAmazonS3 s3Client, bool ownsS3Client)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(s3Client);

            _logger = logger;
            _options = options.Value;
            _s3Client = s3Client;
            _ownsS3Client = ownsS3Client;
        }
        catch (Exception initializationException) when (ownsS3Client && s3Client is not null)
        {
            try
            {
                s3Client.Dispose();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(initializationException, cleanupException);
            }

            throw;
        }
    }

    private static IAmazonS3 CreateS3Client(Setting settings)
    {
        bool hasAccessKey = !string.IsNullOrWhiteSpace(settings.AccessKey);
        bool hasSecretKey = !string.IsNullOrWhiteSpace(settings.SecretKey);
        if (hasAccessKey != hasSecretKey)
        {
            throw new InvalidOperationException(
                "AccessKey and SecretKey must either both be configured or both be omitted.");
        }

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
            _logger.LogInformation(LogEvents.MetadataNotFound, "Amazon cloud-storage object was not found");
            throw new FileNotFoundException($"Not Found - {fileName} in bucket {bucketName}", fileName, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.MetadataFailed, ex, "Amazon cloud-storage metadata request failed");
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

            await _s3Client.PutObjectAsync(request, cancellationToken);

            if (sourceStream.CanSeek) _bytesUploaded.Add(sourceStream.Length);
            _filesUploaded.Add(1);

            return await GetFileMetadataAsync(bucketName, destinationFileName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
             throw;
        }
        catch (Exception e)
        {
             _filesFailed.Add(1);
             _logger.LogError(LogEvents.UploadFailed, e, "Error uploading file");
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
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = fileName
            };
            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            await response.ResponseStream.CopyToAsync(targetStream, cancellationToken);

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
            _logger.LogError(LogEvents.DownloadFailed, e, "Error downloading file");
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
            var request = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = fileName
            };
            await _s3Client.DeleteObjectAsync(request, cancellationToken);
            _filesDeleted.Add(1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.DeleteFailed, e, "Error deleting file");
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
            var request = new CopyObjectRequest
            {
                SourceBucket = sourceBucketName,
                SourceKey = sourceFileName,
                DestinationBucket = destinationBucketName,
                DestinationKey = destinationFileName
            };
            await _s3Client.CopyObjectAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.CopyFailed, e, "Error copying file");
            throw;
        }
    }

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(nameof(GetFileListAsync));

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _filesFailed.Add(1);
                _logger.LogError(LogEvents.ListFailed, e, "Error getting file list");
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
        if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("File name cannot be null or whitespace.", nameof(filename));
        if (string.IsNullOrWhiteSpace(contentType)) throw new ArgumentException("Content type cannot be null or whitespace.", nameof(contentType));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expirationMinutes);
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expirationMinutes);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes)
        };
        return _s3Client.GetPreSignedURL(request);
    }

    private static Setting GetSettings(IOptions<Setting> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Value;
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
            _logger.LogError(LogEvents.UploadFailed, exception, "Could not open the Amazon cloud-storage upload source file");
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources owned by this client.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        if (_ownsS3Client)
        {
            _s3Client.Dispose();
        }
    }
}
