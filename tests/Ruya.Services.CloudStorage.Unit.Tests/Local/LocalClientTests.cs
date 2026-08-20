using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.CloudStorage.Local;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.UnitTests.Local;

[TestClass]
public class LocalClientTests
{
    private Mock<ILogger<Client>> _mockLogger = null!;
    private string _testRootPath = null!;
    private Client _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<Client>>();
        _testRootPath = Path.Combine(Path.GetTempPath(), "LocalClientTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootPath);

        var options = Options.Create(new StorageServiceSettings { Path = _testRootPath });
        var stubMeterFactory = new Ruya.Services.CloudStorage.Tests.Common.StubMeterFactory();
        var stubTracing = new Ruya.Services.CloudStorage.Tests.Common.StubDistributedTracing();
        _client = new Client(_mockLogger.Object, stubTracing, stubMeterFactory, options);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRootPath))
        {
            Directory.Delete(_testRootPath, recursive: true);
        }
    }

    #region GetFileMetadataAsync Tests

    [TestMethod]
    public async Task GetFileMetadataAsync_WithExistingFile_ReturnsCorrectMetadata()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "test-bucket");
        Directory.CreateDirectory(bucketPath);
        var filePath = Path.Combine(bucketPath, "test.txt");
        await File.WriteAllTextAsync(filePath, "Hello World");

        // Act
        var result = await _client.GetFileMetadataAsync("test-bucket", "test.txt");

        // Assert
        Assert.AreEqual("test-bucket", result.Bucket);
        Assert.AreEqual("test.txt", result.Name);
        Assert.AreEqual(11UL, result.Size); // "Hello World" is 11 bytes
        Assert.AreEqual("text/plain", result.ContentType);
        Assert.IsTrue(result.SignedUrl.EndsWith("test.txt"));
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WithNestedFile_ReturnsRelativePath()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        var nestedPath = Path.Combine(bucketPath, "folder", "subfolder");
        Directory.CreateDirectory(nestedPath);
        var filePath = Path.Combine(nestedPath, "deep.txt");
        await File.WriteAllTextAsync(filePath, "content");

        // Act
        var result = await _client.GetFileMetadataAsync("bucket", @"folder\subfolder\deep.txt");

        // Assert
        Assert.IsTrue(result.Name.Contains("deep.txt"));
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenFileNotExists_ThrowsFileNotFoundException()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _client.GetFileMetadataAsync("bucket", "nonexistent.txt"));

        Assert.IsTrue(exception.Message.Contains("nonexistent.txt"));
        Assert.IsTrue(exception.Message.Contains("bucket"));
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenPathValidationFails_RecordsFailure()
    {
        string meterName = $"{nameof(LocalClientTests)}.Metadata.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        Client client = CreateInstrumentedClient(meter);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetFileMetadataAsync("bucket", "../outside.txt"));

        Assert.AreEqual(1, collector.Sum);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public async Task GetFileMetadataAsync_WithInvalidBucketName_ThrowsArgumentException(string? bucketName)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.GetFileMetadataAsync(bucketName!, "file.txt"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public async Task GetFileMetadataAsync_WithInvalidFileName_ThrowsArgumentException(string? fileName)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.GetFileMetadataAsync("bucket", fileName!));
    }

    #endregion

    #region UploadStreamAsync Tests

    [TestMethod]
    public async Task UploadStreamAsync_CreatesFileSuccessfully()
    {
        // Arrange
        var content = "Test content for upload"u8.ToArray();
        using var stream = new MemoryStream(content);

        // Act
        var result = await _client.UploadStreamAsync("uploads", stream, "newfile.txt", "text/plain");

        // Assert
        Assert.AreEqual("uploads", result.Bucket);
        Assert.AreEqual((ulong)content.Length, result.Size);

        var filePath = Path.Combine(_testRootPath, "uploads", "newfile.txt");
        Assert.IsTrue(File.Exists(filePath));
        Assert.AreEqual("Test content for upload", await File.ReadAllTextAsync(filePath));
    }

    [TestMethod]
    public async Task UploadStreamAsync_CreatesDirectoryStructure()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);

        // Act
        await _client.UploadStreamAsync("bucket", stream, @"deep\nested\folder\file.bin", "application/octet-stream");

        // Assert
        var expectedPath = Path.Combine(_testRootPath, "bucket", "deep", "nested", "folder", "file.bin");
        Assert.IsTrue(File.Exists(expectedPath));
    }

    [TestMethod]
    public async Task UploadStreamAsync_OverwritesExistingFile()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        var filePath = Path.Combine(bucketPath, "existing.txt");
        await File.WriteAllTextAsync(filePath, "Original content");

        using var stream = new MemoryStream("New content"u8.ToArray());

        // Act
        await _client.UploadStreamAsync("bucket", stream, "existing.txt", "text/plain");

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        Assert.AreEqual("New content", content);
    }

    [TestMethod]
    public async Task UploadStreamAsync_SeeksStreamToBeginning()
    {
        // Arrange
        using var stream = new MemoryStream("Full content"u8.ToArray());
        stream.Position = 5; // Simulate partially read stream

        // Act
        var result = await _client.UploadStreamAsync("bucket", stream, "file.txt", "text/plain");

        // Assert
        var filePath = Path.Combine(_testRootPath, "bucket", "file.txt");
        var content = await File.ReadAllTextAsync(filePath);
        Assert.AreEqual("Full content", content);
    }

    [TestMethod]
    public async Task UploadStreamAsync_WithEmptyBucket_ThrowsArgumentException()
    {
        using var stream = new MemoryStream(new byte[10]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.UploadStreamAsync("", stream, "file.txt", "text/plain"));
    }

    [TestMethod]
    public async Task UploadStreamAsync_WithEmptyTargetPath_ThrowsArgumentException()
    {
        using var stream = new MemoryStream(new byte[10]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.UploadStreamAsync("bucket", stream, "", "text/plain"));
    }

    [TestMethod]
    public async Task UploadFileAsync_WhenSourceCannotBeOpened_RecordsFailure()
    {
        string meterName = $"{nameof(LocalClientTests)}.UploadFile.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        Client client = CreateInstrumentedClient(meter);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            client.UploadFileAsync("bucket", Path.Combine(_testRootPath, "missing.txt"), "file.txt"));

        Assert.AreEqual(1, collector.Sum);
    }

    #endregion

    #region DownloadFileAsync Tests

    [TestMethod]
    public async Task DownloadFileAsync_WritesToTargetStream()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        var content = "Download me"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(bucketPath, "source.txt"), content);

        using var targetStream = new MemoryStream();

        // Act
        await _client.DownloadFileAsync("bucket", "source.txt", targetStream);

        // Assert
        Assert.AreEqual(content.Length, targetStream.Length);
        Assert.AreEqual(0, targetStream.Position, "Stream should be seeked to beginning");
        CollectionAssert.AreEqual(content, targetStream.ToArray());
    }

    [TestMethod]
    public async Task DownloadFileAsync_WhenFileNotExists_ThrowsException()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        using var targetStream = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _client.DownloadFileAsync("bucket", "missing.txt", targetStream));
    }

    [TestMethod]
    public async Task DownloadFileAsync_WhenDownloadFails_RecordsFailure()
    {
        string meterName = $"{nameof(LocalClientTests)}.Download.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        Client client = CreateInstrumentedClient(meter);
        Directory.CreateDirectory(Path.Combine(_testRootPath, "bucket"));
        using var targetStream = new MemoryStream();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            client.DownloadFileAsync("bucket", "missing.txt", targetStream));

        Assert.AreEqual(1, collector.Sum);
    }

    #endregion

    #region DeleteFileAsync Tests

    [TestMethod]
    public async Task DeleteFileAsync_RemovesFile()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        var filePath = Path.Combine(bucketPath, "to-delete.txt");
        await File.WriteAllTextAsync(filePath, "delete me");

        // Act
        await _client.DeleteFileAsync("bucket", "to-delete.txt");

        // Assert
        Assert.IsFalse(File.Exists(filePath));
    }

    [TestMethod]
    public async Task DeleteFileAsync_CleansUpEmptyDirectories()
    {
        // Arrange
        var nestedPath = Path.Combine(_testRootPath, "bucket", "a", "b", "c");
        Directory.CreateDirectory(nestedPath);
        var filePath = Path.Combine(nestedPath, "file.txt");
        await File.WriteAllTextAsync(filePath, "content");

        // Act
        await _client.DeleteFileAsync("bucket", @"a\b\c\file.txt");

        // Assert
        Assert.IsFalse(File.Exists(filePath));
        // Empty parent directories should be cleaned up
        // (up to but not including the bucket root)
        Assert.IsFalse(Directory.Exists(Path.Combine(_testRootPath, "bucket", "a", "b", "c")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_testRootPath, "bucket", "a", "b")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_testRootPath, "bucket", "a")));
        // Bucket directory should still exist
        Assert.IsTrue(Directory.Exists(Path.Combine(_testRootPath, "bucket")));
    }

    [TestMethod]
    public async Task DeleteFileAsync_WhenDeletingLastRootFile_PreservesBucketDirectory()
    {
        string bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "only-file.txt"), "content");

        await _client.DeleteFileAsync("bucket", "only-file.txt");

        Assert.IsTrue(Directory.Exists(bucketPath));
        Assert.IsFalse(File.Exists(Path.Combine(bucketPath, "only-file.txt")));
    }

    [TestMethod]
    public async Task DeleteFileAsync_WhenConcurrentCleanupAlreadyRemovedDirectory_Succeeds()
    {
        string bucketPath = Path.Combine(_testRootPath, "bucket");
        string nestedPath = Path.Combine(bucketPath, "shared", "leaf");
        Directory.CreateDirectory(nestedPath);
        await File.WriteAllTextAsync(Path.Combine(nestedPath, "file.txt"), "content");

        using var meter = new Meter($"{nameof(LocalClientTests)}.ConcurrentCleanup.{Guid.NewGuid():N}");
        Client client = CreateInstrumentedClient(
            meter,
            enumerateFileSystemEntries: directoryPath =>
            {
                if (directoryPath.Equals(nestedPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(directoryPath);
                    throw new DirectoryNotFoundException("A concurrent cleanup removed the directory.");
                }

                return Directory.EnumerateFileSystemEntries(directoryPath);
            });

        await client.DeleteFileAsync("bucket", @"shared\leaf\file.txt");

        Assert.IsFalse(Directory.Exists(nestedPath));
        Assert.IsTrue(Directory.Exists(bucketPath));
    }

    [TestMethod]
    public async Task DeleteFileAsync_RepeatedConcurrentNestedSiblingDeletes_Succeed()
    {
        string bucketPath = Path.Combine(_testRootPath, "bucket");

        for (int iteration = 0; iteration < 25; iteration++)
        {
            string relativeDirectory = $@"shared\iteration-{iteration}\leaf";
            string nestedPath = Path.Combine(bucketPath, "shared", $"iteration-{iteration}", "leaf");
            Directory.CreateDirectory(nestedPath);
            await File.WriteAllTextAsync(Path.Combine(nestedPath, "first.txt"), "first");
            await File.WriteAllTextAsync(Path.Combine(nestedPath, "second.txt"), "second");

            Task firstDelete = Task.Run(() =>
                _client.DeleteFileAsync("bucket", $@"{relativeDirectory}\first.txt"));
            Task secondDelete = Task.Run(() =>
                _client.DeleteFileAsync("bucket", $@"{relativeDirectory}\second.txt"));

            await Task.WhenAll(firstDelete, secondDelete);

            Assert.IsFalse(Directory.Exists(nestedPath));
            Assert.IsTrue(Directory.Exists(bucketPath));
        }
    }

    [TestMethod]
    public async Task DeleteFileAsync_DoesNotDeleteNonEmptyDirectories()
    {
        // Arrange
        var nestedPath = Path.Combine(_testRootPath, "bucket", "shared");
        Directory.CreateDirectory(nestedPath);
        await File.WriteAllTextAsync(Path.Combine(nestedPath, "file1.txt"), "keep me");
        await File.WriteAllTextAsync(Path.Combine(nestedPath, "file2.txt"), "delete me");

        // Act
        await _client.DeleteFileAsync("bucket", @"shared\file2.txt");

        // Assert
        Assert.IsTrue(File.Exists(Path.Combine(nestedPath, "file1.txt")));
        Assert.IsTrue(Directory.Exists(nestedPath));
    }

    [TestMethod]
    public async Task DeleteFileAsync_WhenFileNotExists_DoesNotThrow()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        // Act & Assert - Should not throw (idempotent)
        await _client.DeleteFileAsync("bucket", "nonexistent.txt");
        
        // Assert
        Assert.IsFalse(File.Exists(Path.Combine(bucketPath, "nonexistent.txt")));
#pragma warning disable CA1873 // Moq expression matchers are not evaluated as production log arguments.
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Debug,
                It.Is<EventId>(eventId => eventId.Id == 8405 && eventId.Name == "LocalDeleteNotFound"),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    #endregion

    #region CopyFileAsync Tests

    [TestMethod]
    public async Task CopyFileAsync_CopiesWithinSameBucket()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        var originalContent = "Original content";
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "original.txt"), originalContent);

        // Act
        await _client.CopyFileAsync("bucket", "original.txt", "bucket", "copy.txt");

        // Assert
        Assert.IsTrue(File.Exists(Path.Combine(bucketPath, "original.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(bucketPath, "copy.txt")));
        Assert.AreEqual(originalContent, await File.ReadAllTextAsync(Path.Combine(bucketPath, "copy.txt")));
    }

    [TestMethod]
    public async Task CopyFileAsync_CopiesBetweenBuckets()
    {
        // Arrange
        var sourceBucket = Path.Combine(_testRootPath, "source-bucket");
        var destBucket = Path.Combine(_testRootPath, "dest-bucket");
        Directory.CreateDirectory(sourceBucket);
        Directory.CreateDirectory(destBucket);

        var content = "Cross-bucket content";
        await File.WriteAllTextAsync(Path.Combine(sourceBucket, "file.txt"), content);

        // Act
        await _client.CopyFileAsync("source-bucket", "file.txt", "dest-bucket", "copied.txt");

        // Assert
        Assert.IsTrue(File.Exists(Path.Combine(destBucket, "copied.txt")));
        Assert.AreEqual(content, await File.ReadAllTextAsync(Path.Combine(destBucket, "copied.txt")));
    }

    [TestMethod]
    public async Task CopyFileAsync_CreatesDestinationDirectory()
    {
        // Arrange
        var sourceBucket = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(sourceBucket);
        await File.WriteAllTextAsync(Path.Combine(sourceBucket, "source.txt"), "content");

        // Act
        await _client.CopyFileAsync("bucket", "source.txt", "bucket", @"new\folder\dest.txt");

        // Assert
        var destPath = Path.Combine(sourceBucket, "new", "folder", "dest.txt");
        Assert.IsTrue(File.Exists(destPath));
    }

    [TestMethod]
    public async Task CopyFileAsync_WhenSourceNotExists_ThrowsFileNotFoundException()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _client.CopyFileAsync("bucket", "missing.txt", "bucket", "copy.txt"));
    }

    [TestMethod]
    public async Task CopyFileAsync_OverwritesExistingDestination()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "source.txt"), "New content");
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "dest.txt"), "Old content");

        // Act
        await _client.CopyFileAsync("bucket", "source.txt", "bucket", "dest.txt");

        // Assert
        Assert.AreEqual("New content", await File.ReadAllTextAsync(Path.Combine(bucketPath, "dest.txt")));
    }

    #endregion

    #region GetFileListAsync Tests

    [TestMethod]
    public async Task GetFileListAsync_ReturnsAllFilesInBucket()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "file1.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "file2.txt"), "b");
        Directory.CreateDirectory(Path.Combine(bucketPath, "folder"));
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "folder", "file3.txt"), "c");

        // Act
        var files = new List<CloudFileMetadata>();
        await foreach (var file in _client.GetFileListAsync("bucket"))
        {
            files.Add(file);
        }

        // Assert
        Assert.AreEqual(3, files.Count);
        Assert.IsTrue(files.Any(f => f.Name == "file1.txt"));
        Assert.IsTrue(files.Any(f => f.Name == "file2.txt"));
        Assert.IsTrue(files.Any(f => f.Name.Contains("file3.txt")));
    }

    [TestMethod]
    public async Task GetFileListAsync_WithPrefix_FiltersFiles()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(Path.Combine(bucketPath, "logs"));
        Directory.CreateDirectory(Path.Combine(bucketPath, "data"));
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "logs", "app.log"), "log");
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "data", "file.csv"), "data");

        // Act
        var files = new List<CloudFileMetadata>();
        await foreach (var file in _client.GetFileListAsync("bucket", "logs"))
        {
            files.Add(file);
        }

        // Assert
        Assert.AreEqual(1, files.Count);
        Assert.IsTrue(files[0].Name.Contains("app.log"));
    }

    [TestMethod]
    public async Task GetFileListAsync_WhenBucketNotExists_ReturnsEmpty()
    {
        // Act
        var files = new List<CloudFileMetadata>();
        await foreach (var file in _client.GetFileListAsync("nonexistent-bucket"))
        {
            files.Add(file);
        }

        // Assert
        Assert.AreEqual(0, files.Count);
    }

    [TestMethod]
    public async Task GetFileListAsync_WithCancellation_StopsEnumeration()
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        for (int i = 0; i < 10; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(bucketPath, $"file{i}.txt"), "x");
        }

        var cts = new CancellationTokenSource();
        var filesReturned = 0;

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var file in _client.GetFileListAsync("bucket", null, cts.Token))
            {
                filesReturned++;
                if (filesReturned == 2)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.AreEqual(2, filesReturned);
    }

    [TestMethod]
    public async Task GetFileListAsync_WhenEnumerationFails_RecordsFailure()
    {
        string meterName = $"{nameof(LocalClientTests)}.List.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        Client client = CreateInstrumentedClient(
            meter,
            _ => new ThrowingEnumerable<string>(new IOException("enumeration failed")));
        Directory.CreateDirectory(Path.Combine(_testRootPath, "bucket"));

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (CloudFileMetadata _ in client.GetFileListAsync("bucket"))
            {
                Assert.Fail("The failing enumerator must not yield an item.");
            }
        });

        Assert.AreEqual(1, collector.Sum);
    }

    [TestMethod]
    public async Task GetFileListAsync_WhenCallerCancels_DoesNotRecordFailure()
    {
        string meterName = $"{nameof(LocalClientTests)}.ListCancellation.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        Client client = CreateInstrumentedClient(meter);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (CloudFileMetadata _ in client.GetFileListAsync("bucket", cancellationToken: cancellation.Token))
            {
                Assert.Fail("The canceled enumerator must not yield an item.");
            }
        });

        Assert.AreEqual(0, collector.Sum);
    }

    #endregion

    #region GetSignedUploadUrl Tests

    [TestMethod]
    public void GetSignedUploadUrl_ReturnsLocalFilePath()
    {
        // Act
        var url = _client.GetSignedUploadUrl("bucket", "file.txt", "text/plain", 60);

        // Assert
        var expectedPath = Path.Combine(_testRootPath, "bucket", "file.txt");
        Assert.AreEqual(expectedPath, url);
    }

    #endregion

    #region Edge Cases and Security

    [TestMethod]
    [DataRow(@"..\secret.txt")]
    [DataRow("../secret.txt")]
    public async Task GetFileMetadataAsync_WhenFileEscapesBucket_ThrowsArgumentException(string traversalPath)
    {
        // Arrange
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        // Create a file that should NOT be accessible
        var secretPath = Path.Combine(_testRootPath, "secret.txt");
        await File.WriteAllTextAsync(secretPath, "secret data");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.GetFileMetadataAsync("bucket", traversalPath));
    }

    [TestMethod]
    [DataRow(@"..\bucket2\secret.txt")]
    [DataRow("../bucket2/secret.txt")]
    public async Task GetFileMetadataAsync_WhenPathUsesBucketNamePrefix_ThrowsArgumentException(string traversalPath)
    {
        var siblingBucketPath = Path.Combine(_testRootPath, "bucket2");
        Directory.CreateDirectory(siblingBucketPath);
        await File.WriteAllTextAsync(Path.Combine(siblingBucketPath, "secret.txt"), "secret data");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.GetFileMetadataAsync("bucket", traversalPath));
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenBucketEscapesConfiguredRoot_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.GetFileMetadataAsync("..", "secret.txt"));
    }

    [TestMethod]
    public async Task GetFileListAsync_WhenBucketContainsDirectoryLink_DoesNotTraverseLink()
    {
        string bucketPath = Path.Combine(_testRootPath, "bucket");
        string outsidePath = Path.Combine(_testRootPath, "outside");
        string linkPath = Path.Combine(bucketPath, "linked");
        Directory.CreateDirectory(bucketPath);
        Directory.CreateDirectory(outsidePath);
        await File.WriteAllTextAsync(Path.Combine(outsidePath, "secret.txt"), "secret");

        try
        {
            Directory.CreateSymbolicLink(linkPath, outsidePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Some Windows hosts do not permit unprivileged symbolic-link creation.
            return;
        }

        var files = new List<CloudFileMetadata>();
        await foreach (CloudFileMetadata file in _client.GetFileListAsync("bucket"))
        {
            files.Add(file);
        }

        Assert.IsFalse(files.Any(file => file.Name.Contains("secret.txt", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task UploadFileAsync_WithVeryLongPath_HandlesGracefully()
    {
        // Arrange
        var longFolderName = new string('a', 200);
        using var stream = new MemoryStream(new byte[10]);

        // Act & Assert - Should throw PathTooLongException or similar on Windows
        try
        {
            await _client.UploadStreamAsync("bucket", stream, $@"{longFolderName}\{longFolderName}\file.txt", "text/plain");
        }
        catch (Exception ex) when (ex is PathTooLongException || ex is DirectoryNotFoundException || ex is IOException)
        {
            // Expected on Windows when path exceeds MAX_PATH
        }
    }

    #endregion

    private Client CreateInstrumentedClient(
        Meter meter,
        Func<string, IEnumerable<string>>? enumerateFiles = null,
        Func<string, IEnumerable<string>>? enumerateFileSystemEntries = null)
    {
        enumerateFiles ??= rootPath => Directory.EnumerateFiles(
            rootPath,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            });
        enumerateFileSystemEntries ??= Directory.EnumerateFileSystemEntries;

        using var meterFactory = new FixedMeterFactory(meter);
        return new Client(
            _mockLogger.Object,
            new Ruya.Services.CloudStorage.Tests.Common.StubDistributedTracing(),
            meterFactory,
            Options.Create(new StorageServiceSettings { Path = _testRootPath }),
            enumerateFiles,
            enumerateFileSystemEntries);
    }
}
