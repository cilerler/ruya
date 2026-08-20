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

public class Client : ICloudFileService, IDisposable
{
    private readonly ILogger _logger;
    private readonly IDistributedTracing _tracer;
    private readonly Meter _meter;
    private readonly StorageClient _storageClient;
    private readonly UrlSigner _urlSigner;
    private readonly bool _ownsStorageClient;
    private int _disposeState;
    private readonly Counter<long> _filesUploaded;
    private readonly Counter<long> _bytesUploaded;
    private readonly Counter<long> _filesDownloaded;
    private readonly Counter<long> _filesDeleted;
    private readonly Counter<long> _filesFailed;

    public Client(ILogger<Client> logger, IDistributedTracing distributedTracing, IMeterFactory meterFactory, IOptions<StorageServiceSettings> options)
        : this(logger, distributedTracing, meterFactory, options, CreateOwnedDependencies(GetSettings(options)))
    {
    }

    public Client(ILogger<Client> logger, IDistributedTracing distributedTracing, IMeterFactory meterFactory, IOptions<StorageServiceSettings> options, StorageClient storageClient, UrlSigner urlSigner)
        : this(logger, distributedTracing, meterFactory, options, storageClient, urlSigner, ownsStorageClient: false)
    {
    }

    internal Client(
        ILogger<Client> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<StorageServiceSettings> options,
        StorageClient storageClient,
        UrlSigner urlSigner,
        bool ownsStorageClient)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(distributedTracing);
            ArgumentNullException.ThrowIfNull(meterFactory);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(storageClient);
            ArgumentNullException.ThrowIfNull(urlSigner);

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
            _storageClient = storageClient;
            _urlSigner = urlSigner;
            _ownsStorageClient = ownsStorageClient;

            _filesUploaded = _meter.CreateCounter<long>("files_uploaded");
            _bytesUploaded = _meter.CreateCounter<long>("bytes_uploaded");
            _filesDownloaded = _meter.CreateCounter<long>("files_downloaded");
            _filesDeleted = _meter.CreateCounter<long>("files_deleted");
            _filesFailed = _meter.CreateCounter<long>("files_failed");
        }
        catch (Exception initializationException) when (ownsStorageClient && storageClient is not null)
        {
            try
            {
                storageClient.Dispose();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(initializationException, cleanupException);
            }

            throw;
        }
    }

    private Client(
        ILogger<Client> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<StorageServiceSettings> options,
        OwnedDependencies dependencies)
        : this(
            logger,
            distributedTracing,
            meterFactory,
            options,
            dependencies.StorageClient,
            dependencies.UrlSigner,
            ownsStorageClient: true)
    {
    }

    private sealed record OwnedDependencies(StorageClient StorageClient, UrlSigner UrlSigner);

    private static StorageServiceSettings GetSettings(IOptions<StorageServiceSettings> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Value;
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

    private static OwnedDependencies CreateOwnedDependencies(StorageServiceSettings settings)
    {
        StorageClient storageClient = CreateStorageClient(settings);
        try
        {
            return new OwnedDependencies(storageClient, CreateUrlSigner(settings));
        }
        catch
        {
            storageClient.Dispose();
            throw;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException gex)
        {
            if (gex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                _filesFailed.Add(1);
                _logger.LogInformation(LogEvents.MetadataNotFound, "Google cloud-storage object was not found");
                throw new FileNotFoundException($"Not Found - {fileName} in bucket {bucketName}", fileName, gex);
            }

            _filesFailed.Add(1);
            _logger.LogError(LogEvents.MetadataFailed, gex, "Google cloud-storage metadata request failed");
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.MetadataFailed, exception, "Google cloud-storage metadata request failed");
            throw;
        }
    }

    public async Task<CloudFileMetadata> UploadFileAsync(string bucketName, string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(UploadFileAsync));

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
        using var activity = _tracer.StartActivity(nameof(UploadStreamAsync));

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

            Object upload = await _storageClient.UploadObjectAsync(bucketName, destinationFileName, contentType, sourceStream, progress: null, cancellationToken: cancellationToken);

            if (sourceStream.CanSeek) _bytesUploaded.Add(sourceStream.Length);
            _filesUploaded.Add(1);

            return new CloudFileMetadata(
                bucketName,
                destinationFileName,
                upload.Size,
                GetUpdatedTime(upload),
                upload.ContentType,
                 _urlSigner.Sign(bucketName, destinationFileName, TimeSpan.FromHours(1), HttpMethod.Get)
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException gex)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.UploadFailed, gex, "Google cloud-storage stream upload failed");
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.UploadFailed, exception, "Google cloud-storage stream upload failed");
            throw;
        }
    }

    public async Task DownloadFileAsync(string bucketName, string fileName, Stream targetStream, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(DownloadFileAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(targetStream);

        try
        {
            await _storageClient.DownloadObjectAsync(bucketName, fileName, targetStream, cancellationToken: cancellationToken);

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
        catch (GoogleApiException gex)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.DownloadFailed, gex, "Google cloud-storage download failed");
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.DownloadFailed, exception, "Google cloud-storage download failed");
            throw;
        }
    }

    public async Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(DeleteFileAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            await _storageClient.DeleteObjectAsync(bucketName, fileName, cancellationToken: cancellationToken);
            _filesDeleted.Add(1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException gex)
        {
             if (gex.HttpStatusCode == HttpStatusCode.NotFound)
             {
                 _logger.LogDebug(LogEvents.DeleteNotFound, "Google cloud-storage object was already absent while deleting a file");
                 return;
             }

            _filesFailed.Add(1);
            _logger.LogError(LogEvents.DeleteFailed, gex, "Google cloud-storage delete failed");
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.DeleteFailed, exception, "Google cloud-storage delete failed");
            throw;
        }
    }

    public async Task CopyFileAsync(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(CopyFileAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationBucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFileName);

        try
        {
            await _storageClient.CopyObjectAsync(sourceBucketName, sourceFileName, destinationBucketName, destinationFileName, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException gex)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.CopyFailed, gex, "Google cloud-storage copy failed");
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.CopyFailed, exception, "Google cloud-storage copy failed");
            throw;
        }
    }

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(GetFileListAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);


        IAsyncEnumerable<Object> fileList;
        try
        {
             fileList = _storageClient.ListObjectsAsync(bucketName, prefix);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.ListFailed, e, "Google cloud-storage listing failed");
            throw;
        }

        IAsyncEnumerator<Object> enumerator;
        try
        {
            enumerator = fileList.GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.ListFailed, exception, "Google cloud-storage listing failed");
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

                    Object file = enumerator.Current;
                    metadata = new CloudFileMetadata(
                        file.Bucket,
                        file.Name,
                        file.Size,
                        GetUpdatedTime(file),
                        file.ContentType,
                         _urlSigner.Sign(file.Bucket, file.Name, TimeSpan.FromHours(1), HttpMethod.Get)
                    );
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _filesFailed.Add(1);
                    _logger.LogError(LogEvents.ListFailed, exception, "Google cloud-storage listing failed");
                    throw;
                }

                yield return metadata;
            }
        }
    }

    public string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expirationMinutes);

        var contentHeaders = new Dictionary<string, IEnumerable<string>> { { "Content-Type", new[] { contentType } } };

        string normalizedFileName = filename;
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
            _logger.LogError(LogEvents.UploadFailed, exception, "Could not open the Google cloud-storage upload source file");
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

        if (_ownsStorageClient)
        {
            _storageClient.Dispose();
        }
    }
}
