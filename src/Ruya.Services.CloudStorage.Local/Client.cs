using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeyRed.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.CloudStorage.Abstractions;

using System.Runtime.CompilerServices;

using Ruya.Diagnostics.DistributedTracing;
using Ruya.Primitives;

namespace Ruya.Services.CloudStorage.Local;

/// <summary>
/// Local file system implementation of <see cref="ICloudFileService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>WARNING: This provider is intended for development and single-instance deployments only.</strong>
/// </para>
/// <para>
/// This implementation is NOT safe for multi-instance (Kubernetes) deployments due to:
/// <list type="bullet">
///   <item><description>No distributed locking - concurrent writes to the same file from multiple pods may corrupt data</description></item>
///   <item><description>Race conditions in file operations - TOCTOU vulnerabilities between existence checks and operations</description></item>
///   <item><description>No atomic operations - partial writes may occur if a pod crashes mid-operation</description></item>
///   <item><description>Directory cleanup is not coordinated across instances</description></item>
/// </list>
/// </para>
/// <para>
/// For production multi-instance deployments, use cloud storage providers (Amazon S3, Azure Blob, Google Cloud Storage)
/// which provide atomic operations and distributed consistency.
/// </para>
/// </remarks>
public class Client : ICloudFileService
{
    private readonly ILogger<Client> _logger;
    private readonly IDistributedTracing _tracer;
    private readonly Meter _meter;
    private readonly StorageServiceSettings _options;
    private const string KeywordService = "Service";
    private const string KeywordComponent = "Component";

    private readonly Counter<long> _filesUploaded;
    private readonly Counter<long> _bytesUploaded;
    private readonly Counter<long> _filesDownloaded;
    private readonly Counter<long> _filesDeleted;
    private readonly Counter<long> _filesFailed;

    public Client(ILogger<Client> logger, IDistributedTracing distributedTracing, IMeterFactory meterFactory, IOptions<StorageServiceSettings> options)
    {
        _logger = logger;
        _tracer = distributedTracing;
        _meter = meterFactory.Create(new MeterOptions(Primitives.Startup.AssemblyName)
        {
            Version = Primitives.Startup.AssemblyVersion,
            Tags = new TagList() { { GetType().Name.EndsWith(KeywordService) ? KeywordService : KeywordComponent, GetType().Name } }
        });
        _options = options.Value;

        _filesUploaded = _meter.CreateCounter<long>("files_uploaded");
        _bytesUploaded = _meter.CreateCounter<long>("bytes_uploaded");
        _filesDownloaded = _meter.CreateCounter<long>("files_downloaded");
        _filesDeleted = _meter.CreateCounter<long>("files_deleted");
        _filesFailed = _meter.CreateCounter<long>("files_failed");
    }

    private string GetRootPath(string bucketName) => Path.Combine(_options.Path, bucketName);

    /// <summary>
    /// Validates that a file path does not escape the bucket root directory (path traversal prevention).
    /// </summary>
    /// <param name="fullPath">The full resolved path to validate.</param>
    /// <param name="rootPath">The bucket root path that the file must be within.</param>
    /// <param name="fileName">The original file name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown if the path attempts to escape the bucket root.</exception>
    private static void ValidatePathWithinRoot(string fullPath, string rootPath, string fileName)
    {
        string normalizedFullPath = Path.GetFullPath(fullPath);
        string normalizedRootPath = Path.GetFullPath(rootPath);

        if (!normalizedFullPath.StartsWith(normalizedRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid file path: '{fileName}' attempts to access outside the bucket root.", nameof(fileName));
        }
    }

    public Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(GetFileMetadataAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("fileName", fileName);

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            string rootPath = GetRootPath(bucketName);
            string filePath = Path.Combine(rootPath, fileName);
            ValidatePathWithinRoot(filePath, rootPath, fileName);
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                 throw new FileNotFoundException($"File Not Found - {fileName} in bucket {bucketName}", fileName);
            }

            var output = new CloudFileMetadata(
                bucketName,
                GetFileName(fileInfo.FullName, bucketName),
                (ulong)fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                MimeTypesMap.GetMimeType(fileInfo.Name),
                fileInfo.FullName
            );
            return Task.FromResult(output);
        }
        catch (FileNotFoundException fnfe)
        {
            _filesFailed.Add(1);
            _logger.LogError(fnfe, "File Not Found - {fileName} in bucket {bucketName}", fileName, bucketName);
            throw;
        }
    }

    public async Task<CloudFileMetadata> UploadFileAsync(string bucketName, string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(UploadFileAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("sourcePath", sourcePath);
        activity.SetTag("targetPath", targetPath);

        string contentType = "application/octet-stream";
        try
        {
            contentType = MimeTypesMap.GetMimeType(sourcePath);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "There is an error occured while trying to retrieve MimeType");
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

        string rootPath = GetRootPath(bucketName);
        string destinationPath = Path.Combine(rootPath, targetPath);
        ValidatePathWithinRoot(destinationPath, rootPath, targetPath);

        try
        {
            string? directoryPath = Path.GetDirectoryName(destinationPath);
            if (directoryPath != null && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            using (FileStream fileStream = File.Create(destinationPath))
            {
                if (sourceStream.CanSeek)
                {
                    sourceStream.Seek(0, SeekOrigin.Begin);
                }

                await sourceStream.CopyToAsync(fileStream, cancellationToken);

                if (sourceStream.CanSeek)
                {
                    _bytesUploaded.Add(sourceStream.Length);
                }
            }
            _filesUploaded.Add(1);
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(e, e.Message);
            throw;
        }

        var fileInfo = new FileInfo(destinationPath);
        return new CloudFileMetadata(
            bucketName,
            GetFileName(fileInfo.FullName, bucketName),
            (ulong)fileInfo.Length,
            fileInfo.LastWriteTimeUtc,
            contentType ?? MimeTypesMap.GetMimeType(fileInfo.Name),
            fileInfo.FullName
        );
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
            string rootPath = GetRootPath(bucketName);
            string filePath = Path.Combine(rootPath, fileName);
            ValidatePathWithinRoot(filePath, rootPath, fileName);
            using (FileStream fileStream = File.OpenRead(filePath))
            {
                await fileStream.CopyToAsync(targetStream, cancellationToken);
            }
            _filesDownloaded.Add(1);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }

         if (targetStream.CanSeek)
         {
             targetStream.Seek(0, SeekOrigin.Begin);
         }
    }

    public Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(DeleteFileAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("fileName", fileName);

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            string rootPath = GetRootPath(bucketName);
            string filePath = Path.Combine(rootPath, fileName);
            ValidatePathWithinRoot(filePath, rootPath, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                EnsureEmptyDirectoriesDeleted(filePath, bucketName);
                _filesDeleted.Add(1);
            }
            else
            {
                // Idempotent behavior: log but don't throw if file doesn't exist
                // This is consistent with cloud provider behavior (S3, Azure, GCS)
                _logger.LogDebug("File does not exist, skipping delete - {FileName} in bucket {BucketName}", fileName, bucketName);
            }
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
             _filesFailed.Add(1);
             _logger.LogError(ex, "Error deleting file");
             throw;
        }
    }

    public Task CopyFileAsync(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(CopyFileAsync));
        activity.SetTag("sourceBucket", sourceBucketName);
        activity.SetTag("sourceFile", sourceFileName);
        activity.SetTag("destinationBucket", destinationBucketName);

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationBucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFileName);

        string sourceRootPath = GetRootPath(sourceBucketName);
        string destRootPath = GetRootPath(destinationBucketName);
        string sourcePath = Path.Combine(sourceRootPath, sourceFileName);
        string destinationPath = Path.Combine(destRootPath, destinationFileName);

        ValidatePathWithinRoot(sourcePath, sourceRootPath, sourceFileName);
        ValidatePathWithinRoot(destinationPath, destRootPath, destinationFileName);

        try
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source file does not exist", sourcePath);

            string? destDir = Path.GetDirectoryName(destinationPath);
            if (destDir != null && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(sourcePath, destinationPath, true);
            return Task.CompletedTask;
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(e, $"{nameof(CopyFileAsync)} : An error occured while moving {{source}} to {{destination}}", sourcePath, destinationPath);
            throw;
        }
    }

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(GetFileListAsync));
        activity.SetTag("bucket", bucketName);
        activity.SetTag("prefix", prefix);

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);

        string rootPath = GetRootPath(bucketName);

        // Validate prefix doesn't attempt path traversal
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            string prefixPath = Path.Combine(rootPath, prefix);
            ValidatePathWithinRoot(prefixPath, rootPath, prefix);
        }

        if (!Directory.Exists(rootPath))
        {
             yield break;
        }

        IEnumerable<string> files;
        try
        {
             files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(e, e.Message);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            string modifiedPrefix = Path.Combine(rootPath, prefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            files = files.Where(f => f.StartsWith(modifiedPrefix));
        }

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CloudFileMetadata metadata;
            try
            {
                var fileInfo = new FileInfo(file);
                metadata = new CloudFileMetadata(
                    bucketName,
                    GetFileName(fileInfo.FullName, bucketName),
                    (ulong)fileInfo.Length,
                    fileInfo.LastWriteTimeUtc,
                    MimeTypesMap.GetMimeType(fileInfo.Name),
                    fileInfo.FullName
                );
            }
            catch (Exception e)
            {
                 _logger.LogError(e, "Error processing file {file}", file);
                 continue;
            }

            yield return metadata;
        }
    }

    public string GetSignedUploadUrl(string bucketName, string filename, string contentType, int expirationMinutes = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        string rootPath = GetRootPath(bucketName);
        string filePath = Path.Combine(rootPath, filename);
        ValidatePathWithinRoot(filePath, rootPath, filename);

        return filePath;
    }

    private string GetFileName(string fullName, string bucketName)
    {
        return fullName.Replace(GetRootPath(bucketName), string.Empty).TrimStart(Path.DirectorySeparatorChar);
    }

    private void EnsureEmptyDirectoriesDeleted(string filePath, string bucketName)
    {
        string? directoryPath = Path.GetDirectoryName(filePath);
        if (directoryPath == null) return;

        string rootPath = GetRootPath(bucketName);

        bool entriesExist;
        do
        {
            entriesExist = Directory.EnumerateFileSystemEntries(directoryPath).Any();
            if (entriesExist) continue;

            try
            {
                _logger.LogInformation("Folder {folder} deleted.", directoryPath);
                Directory.Delete(directoryPath);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "There is an error occured while deleting a folder {folder}", directoryPath);
                entriesExist = true;
            }

            if (directoryPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) break;

            string? parent = Path.GetDirectoryName(directoryPath);
            if (string.IsNullOrEmpty(parent)) break;
            directoryPath = parent;

        } while (!entriesExist && !directoryPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase) && directoryPath.StartsWith(rootPath));
    }
}
