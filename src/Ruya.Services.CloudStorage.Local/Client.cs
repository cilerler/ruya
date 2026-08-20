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
///   <item><description>File-system path validation cannot prevent another process from replacing a checked path segment</description></item>
///   <item><description>Directory cleanup is not coordinated across instances</description></item>
/// </list>
/// Upload and copy operations write to a same-directory temporary file before atomically moving it into place.
/// </para>
/// <para>
/// For production multi-instance deployments, use cloud storage providers (Amazon S3, Azure Blob, Google Cloud Storage)
/// which provide atomic operations and distributed consistency.
/// </para>
/// </remarks>
public class Client : ICloudFileService
{
    private static readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly ILogger<Client> _logger;
    private readonly IDistributedTracing _tracer;
    private readonly Meter _meter;
    private readonly string _basePath;
    private readonly Func<string, IEnumerable<string>> _enumerateFiles;
    private readonly Func<string, IEnumerable<string>> _enumerateFileSystemEntries;
    private const string KeywordService = "Service";
    private const string KeywordComponent = "Component";

    private readonly Counter<long> _filesUploaded;
    private readonly Counter<long> _bytesUploaded;
    private readonly Counter<long> _filesDownloaded;
    private readonly Counter<long> _filesDeleted;
    private readonly Counter<long> _filesFailed;

    public Client(ILogger<Client> logger, IDistributedTracing distributedTracing, IMeterFactory meterFactory, IOptions<StorageServiceSettings> options)
        : this(logger, distributedTracing, meterFactory, options, EnumerateFiles, EnumerateFileSystemEntries)
    {
    }

    internal Client(
        ILogger<Client> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<StorageServiceSettings> options,
        Func<string, IEnumerable<string>> enumerateFiles,
        Func<string, IEnumerable<string>> enumerateFileSystemEntries)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(distributedTracing);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(enumerateFiles);
        ArgumentNullException.ThrowIfNull(enumerateFileSystemEntries);

        _logger = logger;
        _tracer = distributedTracing;
        _meter = meterFactory.Create(new MeterOptions(Primitives.Startup.AssemblyName)
        {
            Version = Primitives.Startup.AssemblyVersion,
            Tags = new TagList() { { GetType().Name.EndsWith(KeywordService) ? KeywordService : KeywordComponent, GetType().Name } }
        });
        _basePath = Path.GetFullPath(options.Value.Path);
        _enumerateFiles = enumerateFiles;
        _enumerateFileSystemEntries = enumerateFileSystemEntries;

        _filesUploaded = _meter.CreateCounter<long>("files_uploaded");
        _bytesUploaded = _meter.CreateCounter<long>("bytes_uploaded");
        _filesDownloaded = _meter.CreateCounter<long>("files_downloaded");
        _filesDeleted = _meter.CreateCounter<long>("files_deleted");
        _filesFailed = _meter.CreateCounter<long>("files_failed");
    }

    private static IEnumerable<string> EnumerateFiles(string rootPath) =>
        Directory.EnumerateFiles(
            rootPath,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            });

    private static IEnumerable<string> EnumerateFileSystemEntries(string directoryPath) =>
        Directory.EnumerateFileSystemEntries(directoryPath);

    private string GetRootPath(string bucketName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);

        string rootPath = Path.GetFullPath(Path.Combine(_basePath, NormalizeRelativePath(bucketName)));
        ValidatePathWithinRoot(rootPath, _basePath, nameof(bucketName), allowRoot: false);
        return rootPath;
    }

    private static string NormalizeRelativePath(string path) => path
        .Replace('\\', Path.DirectorySeparatorChar)
        .Replace('/', Path.DirectorySeparatorChar);

    /// <summary>
    /// Validates that a file path does not escape the bucket root directory (path traversal prevention).
    /// </summary>
    /// <param name="fullPath">The full resolved path to validate.</param>
    /// <param name="rootPath">The bucket root path that the file must be within.</param>
    /// <param name="parameterName">The public parameter being validated.</param>
    /// <param name="allowRoot">Whether a path equal to <paramref name="rootPath"/> is valid.</param>
    /// <exception cref="ArgumentException">Thrown if the path attempts to escape the bucket root.</exception>
    private static void ValidatePathWithinRoot(
        string fullPath,
        string rootPath,
        string parameterName = "fileName",
        bool allowRoot = true)
    {
        string normalizedFullPath = Path.GetFullPath(fullPath);
        string normalizedRootPath = Path.GetFullPath(rootPath);
        string relativePath = Path.GetRelativePath(normalizedRootPath, normalizedFullPath);

        if ((!allowRoot && relativePath == ".") ||
            Path.IsPathRooted(relativePath) ||
            relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("The path must remain within the configured storage root.", parameterName);
        }

        RejectReparsePoints(normalizedRootPath, normalizedFullPath, parameterName);
    }

    private static void RejectReparsePoints(string rootPath, string fullPath, string parameterName)
    {
        string relativePath = Path.GetRelativePath(rootPath, fullPath);
        string currentPath = rootPath;

        foreach (string segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if ((File.Exists(currentPath) || Directory.Exists(currentPath)) &&
                File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ArgumentException(
                    "The path cannot traverse a symbolic link or reparse point.",
                    parameterName);
            }
        }
    }

    private static string CreateTemporaryPath(string destinationPath)
    {
        string directoryPath = Path.GetDirectoryName(destinationPath)!;
        return Path.Combine(directoryPath, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.upload");
    }

    private static async Task MoveIntoPlaceAsync(
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 8;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, destinationPath, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                attempt < maxAttempts &&
                exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 5), cancellationToken);
            }
        }

        throw new InvalidOperationException("The local cloud-storage move did not complete.");
    }

    public Task<CloudFileMetadata> GetFileMetadataAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(GetFileMetadataAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string rootPath = GetRootPath(bucketName);
            string filePath = Path.Combine(rootPath, NormalizeRelativePath(fileName));
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
        catch (FileNotFoundException)
        {
            _filesFailed.Add(1);
            _logger.LogInformation(LogEvents.MetadataNotFound, "Local cloud-storage object was not found");
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.MetadataFailed, exception, "Local cloud-storage metadata request failed");
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
            _logger.LogWarning(LogEvents.MimeTypeFailed, e, "There is an error occured while trying to retrieve MimeType");
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

        string rootPath = GetRootPath(bucketName);
        string destinationPath = Path.Combine(rootPath, NormalizeRelativePath(targetPath));
        ValidatePathWithinRoot(destinationPath, rootPath, targetPath);

        string? temporaryPath = null;
        try
        {
            string? directoryPath = Path.GetDirectoryName(destinationPath);
            if (directoryPath != null && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            temporaryPath = CreateTemporaryPath(destinationPath);
            await using (FileStream fileStream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (sourceStream.CanSeek)
                {
                    sourceStream.Seek(0, SeekOrigin.Begin);
                }

                await sourceStream.CopyToAsync(fileStream, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);
            }

            await MoveIntoPlaceAsync(temporaryPath, destinationPath, cancellationToken);
            temporaryPath = null;

            _bytesUploaded.Add(new FileInfo(destinationPath).Length);
            _filesUploaded.Add(1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.UploadFailed, e, "Local cloud-storage upload failed");
            throw;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(LogEvents.UploadCleanupFailed, cleanupException, "Could not remove a temporary cloud-storage upload file");
                }
            }
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

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(targetStream);

        try
        {
            string rootPath = GetRootPath(bucketName);
            string filePath = Path.Combine(rootPath, NormalizeRelativePath(fileName));
            ValidatePathWithinRoot(filePath, rootPath, fileName);
            await using (FileStream fileStream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await fileStream.CopyToAsync(targetStream, cancellationToken);
            }

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
            _logger.LogError(LogEvents.DownloadFailed, e, "Local cloud-storage download failed");
            throw;
        }

    }

    public Task DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(DeleteFileAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string rootPath = GetRootPath(bucketName);
            string filePath = Path.Combine(rootPath, NormalizeRelativePath(fileName));
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
                _logger.LogDebug(LogEvents.DeleteNotFound, "Local cloud-storage object was already absent while deleting a file");
            }
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
             _filesFailed.Add(1);
             _logger.LogError(LogEvents.DeleteFailed, ex, "Error deleting file");
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

        string sourceRootPath = GetRootPath(sourceBucketName);
        string destRootPath = GetRootPath(destinationBucketName);
        string sourcePath = Path.Combine(sourceRootPath, NormalizeRelativePath(sourceFileName));
        string destinationPath = Path.Combine(destRootPath, NormalizeRelativePath(destinationFileName));

        ValidatePathWithinRoot(sourcePath, sourceRootPath, sourceFileName);
        ValidatePathWithinRoot(destinationPath, destRootPath, destinationFileName);

        string? temporaryPath = null;
        try
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source file does not exist", sourcePath);

            string? destDir = Path.GetDirectoryName(destinationPath);
            if (destDir != null && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            temporaryPath = CreateTemporaryPath(destinationPath);
            await using (FileStream sourceStream = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream destinationStream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                await destinationStream.FlushAsync(cancellationToken);
            }

            await MoveIntoPlaceAsync(temporaryPath, destinationPath, cancellationToken);
            temporaryPath = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.CopyFailed, e, "Local cloud-storage copy failed");
            throw;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(LogEvents.CopyCleanupFailed, cleanupException, "Could not remove a temporary cloud-storage copy file");
                }
            }
        }
    }

    public async IAsyncEnumerable<CloudFileMetadata> GetFileListAsync(string bucketName, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _tracer.StartActivity(nameof(GetFileListAsync));

        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        cancellationToken.ThrowIfCancellationRequested();

        string rootPath = GetRootPath(bucketName);

        // Validate prefix doesn't attempt path traversal
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            string prefixPath = Path.Combine(rootPath, NormalizeRelativePath(prefix));
            ValidatePathWithinRoot(prefixPath, rootPath, prefix);
        }

        if (!Directory.Exists(rootPath))
        {
             yield break;
        }

        IEnumerable<string> files;
        try
        {
             files = _enumerateFiles(rootPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.ListFailed, e, "Local cloud-storage listing failed");
            throw;
        }

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            string modifiedPrefix = Path.Combine(rootPath, NormalizeRelativePath(prefix));
            files = files.Where(f => f.StartsWith(modifiedPrefix, _pathComparison));
        }

        IEnumerator<string> enumerator;
        try
        {
            enumerator = files.GetEnumerator();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _filesFailed.Add(1);
            _logger.LogError(LogEvents.ListFailed, exception, "Local cloud-storage listing failed");
            throw;
        }

        using (enumerator)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string file;
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    file = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _filesFailed.Add(1);
                    _logger.LogError(LogEvents.ListFailed, exception, "Local cloud-storage listing failed");
                    throw;
                }

                CloudFileMetadata metadata;
                try
                {
                    ValidatePathWithinRoot(file, rootPath, prefix ?? bucketName);
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
                     _filesFailed.Add(1);
                     _logger.LogError(LogEvents.MetadataProjectionFailed, e, "Local cloud-storage metadata projection failed");
                     continue;
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

        string rootPath = GetRootPath(bucketName);
        string filePath = Path.Combine(rootPath, NormalizeRelativePath(filename));
        ValidatePathWithinRoot(filePath, rootPath, filename);

        return filePath;
    }

    private string GetFileName(string fullName, string bucketName)
    {
        return PathNormalizer.ToCloudPath(Path.GetRelativePath(GetRootPath(bucketName), fullName));
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
            _logger.LogError(LogEvents.UploadFailed, exception, "Could not open the local cloud-storage upload source file");
            throw;
        }
    }

    private void EnsureEmptyDirectoriesDeleted(string filePath, string bucketName)
    {
        string? directoryPath = Path.GetDirectoryName(filePath);
        if (directoryPath == null) return;

        string rootPath = GetRootPath(bucketName);

        while (!directoryPath.Equals(rootPath, _pathComparison) &&
               !Path.GetRelativePath(rootPath, directoryPath).StartsWith("..", StringComparison.Ordinal))
        {
            string? parentPath = Path.GetDirectoryName(directoryPath);

            try
            {
                if (_enumerateFileSystemEntries(directoryPath).Any())
                {
                    return;
                }

                _logger.LogInformation(LogEvents.EmptyDirectoryRemoved, "Removed an empty local cloud-storage directory");
                Directory.Delete(directoryPath);
            }
            catch (DirectoryNotFoundException)
            {
                // Another concurrent delete already completed this best-effort cleanup.
            }
            catch (Exception e)
            {
                _logger.LogError(LogEvents.EmptyDirectoryRemovalFailed, e, "Could not remove an empty local cloud-storage directory");
                return;
            }

            if (string.IsNullOrEmpty(parentPath)) return;
            directoryPath = parentPath;
        }
    }
}
