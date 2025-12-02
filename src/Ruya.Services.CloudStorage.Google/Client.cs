using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using HeyRed.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.CloudStorage.Abstractions;
using Object = Google.Apis.Storage.v1.Data.Object;

using System.Runtime.CompilerServices;

using Ruya.Diagnostics.DistributedTracing;
using Ruya.Primitives;

namespace Ruya.Services.CloudStorage.Google;

public class Client : ICloudFileService
{
    private readonly ILogger _logger;
    private readonly IDistributedTracing _tracer;
    private readonly Meter _meter;
    private readonly StorageServiceSettings _settings;
    private readonly StorageClient _storageClient;
    private readonly UrlSigner _urlSigner;
    private readonly Counter<long> _filesUploaded;
    private readonly Counter<long> _bytesUploaded;
    private readonly Counter<long> _filesDownloaded;
    private readonly Counter<long> _filesDeleted;
    private readonly Counter<long> _filesFailed;

    public Client(ILogger<Client> logger, IDistributedTracing distributedTracing, IMeterFactory meterFactory, IOptions<StorageServiceSettings> options)
        : this(logger, distributedTracing, meterFactory, options, CreateStorageClient(options.Value), CreateUrlSigner(options.Value))
    {
    }

    public Client(ILogger<Client> logger, IDistributedTracing distributedTracing, IMeterFactory meterFactory, IOptions<StorageServiceSettings> options, StorageClient storageClient, UrlSigner urlSigner)
    {
        _logger = logger;
        _tracer = distributedTracing;
        _meter = meterFactory.Create(new MeterOptions(Startup.AssemblyName)
        {
            Version = Startup.AssemblyVersion,
            Tags = new TagList
                {
                    { "code.namespace", GetType().Namespace },
                    { "code.class", GetType().Name }
                }
        });
        _settings = options.Value;
        _storageClient = storageClient;
        _urlSigner = urlSigner;

        _filesUploaded = _meter.CreateCounter<long>("files_uploaded");
        _bytesUploaded = _meter.CreateCounter<long>("bytes_uploaded");
        _filesDownloaded = _meter.CreateCounter<long>("files_downloaded");
        _filesDeleted = _meter.CreateCounter<long>("files_deleted");
        _filesFailed = _meter.CreateCounter<long>("files_failed");
    }



    private static StorageClient CreateStorageClient(StorageServiceSettings settings)
    {
		string? emulatorHost = Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST");
		if (!string.IsNullOrEmpty(emulatorHost))
		{
			var baseUri = new Uri(emulatorHost);
			var storageUri = new Uri(baseUri, "/storage/v1/");

			var builder = new StorageClientBuilder
			{
				BaseUri = storageUri.ToString(),
				UnauthenticatedAccess = true
			};
			return builder.Build();
		}
		else
		{
	        GoogleCredential credentials = GetGoogleCredentials(settings);
			return StorageClient.Create(credentials);
		}
    }

    private static UrlSigner CreateUrlSigner(StorageServiceSettings settings)
    {
        GoogleCredential credentials = GetGoogleCredentials(settings);

        if (credentials.UnderlyingCredential is ServiceAccountCredential sac)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return UrlSigner.FromServiceAccountCredential(sac);
#pragma warning restore CS0618 // Type or member is obsolete
        }
        else
        {
            return UrlSigner.FromCredential(credentials);
        }
    }

    private static GoogleCredential GetGoogleCredentials(StorageServiceSettings settings)
    {
		return GoogleCredential.FromJson(settings.Credential);
    }

    public async Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(GetFileMetadataAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("fileName", fileName);

         if (string.IsNullOrWhiteSpace(bucketName)) throw new ArgumentException("Bucket name cannot be null or whitespace.", nameof(bucketName));
         if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name cannot be null or whitespace.", nameof(fileName));

        try
        {
            Object file = await _storageClient.GetObjectAsync(bucketName, fileName, cancellationToken: cancellationToken);

            var output = new CloudFileMetadata(
                file.Bucket,
                file.Name,
                file.Size,
                GetUpdatedTime(file),
                file.ContentType,
                _urlSigner.Sign(file.Bucket, file.Name, TimeSpan.FromHours(1), HttpMethod.Get)
            );
            return output;
        }
        catch (GoogleApiException gex)
        {
            if (gex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                _filesFailed.Add(1);
                _logger.LogInformation("Not Found - {fileName} in bucket {bucketName}", fileName, bucketName);
                throw new FileNotFoundException($"Not Found - {fileName} in bucket {bucketName}", fileName, gex);
            }

            _filesFailed.Add(1);
            _logger.LogError(gex, gex.Error.Message);
            throw;
        }
    }

    public async Task<CloudFileMetadata> UploadFileAsync(string bucketName, string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(UploadFileAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("sourcePath", sourcePath);

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
        using var activity = _tracer.StartActivity(nameof(UploadStreamAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("targetPath", targetPath);

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string destinationFileName = PathNormalizer.ToCloudPath(targetPath);

        var progress = new Progress<global::Google.Apis.Upload.IUploadProgress>(p =>
            _logger.LogTrace("destination gs://{bucket}/{destinationFileName}, bytes: {BytesSent}, status: {Status}",
                bucketName, destinationFileName, p.BytesSent, p.Status));

        if (sourceStream.CanSeek)
        {
             sourceStream.Seek(0, SeekOrigin.Begin);
        }

        Object upload;
        try
        {
            upload = await _storageClient.UploadObjectAsync(bucketName, destinationFileName, contentType, sourceStream, progress: progress, cancellationToken: cancellationToken);

            if (sourceStream.CanSeek) _bytesUploaded.Add(sourceStream.Length);
            _filesUploaded.Add(1);
        }
        catch (GoogleApiException gex)
        {
            _filesFailed.Add(1);
            _logger.LogError(gex, "Encountered an error while uploading file stream. {BucketName} {FileName}", bucketName, destinationFileName);
            throw;
        }

        var output = new CloudFileMetadata(
            bucketName,
            destinationFileName,
            upload.Size,
            GetUpdatedTime(upload),
            upload.ContentType,
             _urlSigner.Sign(bucketName, destinationFileName, TimeSpan.FromHours(1), HttpMethod.Get)
        );
        return output;
    }

    public async Task DownloadFileAsync(string bucketName, string fileName, Stream targetStream, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(DownloadFileAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("fileName", fileName);

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            await _storageClient.DownloadObjectAsync(bucketName, fileName, targetStream, cancellationToken: cancellationToken);
            _filesDownloaded.Add(1);
        }
        catch (GoogleApiException gex)
        {
            _filesFailed.Add(1);
            _logger.LogError(gex, "Encountered an error while dowloading file. {BucketName} {FileName}", bucketName, fileName);
            throw;
        }

        if (targetStream.CanSeek)
             targetStream.Seek(0, SeekOrigin.Begin);
    }

    public async Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(DeleteFileAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("fileName", fileName);

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            await _storageClient.DeleteObjectAsync(bucketName, fileName, cancellationToken: cancellationToken);
            _filesDeleted.Add(1);
        }
        catch (GoogleApiException gex)
        {
             if (gex.HttpStatusCode == HttpStatusCode.NotFound)
             {
                 _logger.LogWarning("File Not Found - {fileName} in bucket {bucketName}", fileName, bucketName);
                 return;
             }

            _filesFailed.Add(1);
            _logger.LogError(gex, "Encountered an error while deleting file. {BucketName} {FileName}", bucketName, fileName);
            throw;
        }
    }

    public async Task CopyFileAsync(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(CopyFileAsync));
        activity.SetTag("sourceBucket", sourceBucketName);
        activity.SetTag("destinationBucket", destinationBucketName);

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationBucketName);

        try
        {
            await _storageClient.CopyObjectAsync(sourceBucketName, sourceFileName, destinationBucketName, destinationFileName, cancellationToken: cancellationToken);
        }
        catch (GoogleApiException gex)
        {
            _filesFailed.Add(1);
            _logger.LogError(gex, "Encountered an error while copying file. {SourceBucket} {SourceFile} {DestinationBucket} {DestinationFileName}",
                sourceBucketName, sourceFileName, destinationBucketName, destinationFileName);
            throw;
        }
    }

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(GetFileListAsync));
        activity.SetTag("bucket", bucketName);

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);


        IAsyncEnumerable<Object> fileList;
        try
        {
             fileList = _storageClient.ListObjectsAsync(bucketName, prefix);
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(e, "Encountered an error while getting file list. {Prefix} {BucketName}", prefix, bucketName);
            throw;
        }

        await foreach (var file in fileList.WithCancellation(cancellationToken))
        {
            yield return new CloudFileMetadata(
                file.Bucket,
                file.Name,
                file.Size,
                GetUpdatedTime(file),
                file.ContentType,
                 _urlSigner.Sign(file.Bucket, file.Name, TimeSpan.FromHours(1), HttpMethod.Get)
            );
        }
    }

    public string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        var contentHeaders = new Dictionary<string, IEnumerable<string>> { { "Content-Type", new[] { contentType } } };

        string normalizedFileName = filename.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        UrlSigner.RequestTemplate template = UrlSigner.RequestTemplate
            .FromBucket(bucketName)
            .WithObjectName(normalizedFileName)
            .WithHttpMethod(HttpMethod.Put)
            .WithContentHeaders(contentHeaders);

        return _urlSigner.Sign(template, UrlSigner.Options.FromDuration(TimeSpan.FromMinutes(expirationMinutes)));
    }

    private static DateTime? GetUpdatedTime(Object obj)
    {
        try
        {
            if (obj.UpdatedDateTimeOffset.HasValue) return obj.UpdatedDateTimeOffset.Value.UtcDateTime;
        }
        catch (FormatException)
        {
            // Ignore format exception and try fallback
        }

        return obj.Updated;
    }
}
