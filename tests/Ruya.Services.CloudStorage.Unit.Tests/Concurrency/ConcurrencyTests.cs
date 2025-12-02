using System;
using System.Collections.Generic;
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

namespace Ruya.Services.CloudStorage.UnitTests.Concurrency;

/// <summary>
/// Tests to verify behavior under concurrent operations.
/// These tests simulate multi-threaded and multi-instance scenarios
/// that may occur in Kubernetes deployments.
/// </summary>
[TestClass]
public class ConcurrencyTests
{
    private string _testRootPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "ConcurrencyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRootPath))
        {
            try
            {
                Directory.Delete(_testRootPath, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    #region Local Client Concurrency Tests

    [TestMethod]
    public async Task ConcurrentUploads_ToDifferentFiles_AllSucceed()
    {
        // Arrange
        var client = CreateLocalClient();
        var tasks = new List<Task<CloudFileMetadata>>();
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        const int concurrentUploads = 20;

        // Act
        for (int i = 0; i < concurrentUploads; i++)
        {
            var index = i;
            var task = Task.Run(async () =>
            {
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"Content for file {index}"));
                return await client.UploadStreamAsync("bucket", stream, $"file_{index}.txt", "text/plain");
            });
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.AreEqual(concurrentUploads, results.Length);
        Assert.IsTrue(results.All(r => r != null));

        // Verify all files exist
        for (int i = 0; i < concurrentUploads; i++)
        {
            Assert.IsTrue(File.Exists(Path.Combine(bucketPath, $"file_{i}.txt")));
        }
    }

    [TestMethod]
    public async Task ConcurrentUploads_ToSameFile_LastWriteWins()
    {
        // Arrange
        var client = CreateLocalClient();
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        const int concurrentUploads = 10;
        var tasks = new List<Task>();

        // Act - All write to the same file
        for (int i = 0; i < concurrentUploads; i++)
        {
            var content = $"Content version {i}";
            var task = Task.Run(async () =>
            {
                int retries = 3;
                while (retries > 0)
                {
                    try
                    {
                        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                        await client.UploadStreamAsync("bucket", stream, "shared_file.txt", "text/plain");
                        break;
                    }
                    catch (IOException) when (retries > 1)
                    {
                        retries--;
                        await Task.Delay(10);
                    }
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert - File should exist and contain one of the versions
        var filePath = Path.Combine(bucketPath, "shared_file.txt");
        Assert.IsTrue(File.Exists(filePath));
        var content2 = await File.ReadAllTextAsync(filePath);
        Assert.IsTrue(content2.StartsWith("Content version"));
    }

    [TestMethod]
    public async Task ConcurrentReads_WhileWriting_DoNotCorruptData()
    {
        // Arrange
        var client = CreateLocalClient();
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        var filePath = Path.Combine(bucketPath, "concurrent_read.txt");
        var expectedContent = "Initial content for concurrent reading";
        await File.WriteAllTextAsync(filePath, expectedContent);

        const int concurrentReads = 50;
        var readResults = new List<string>();
        var readTasks = new List<Task>();
        var lockObj = new object();

        // Act - Concurrent reads
        for (int i = 0; i < concurrentReads; i++)
        {
            var task = Task.Run(async () =>
            {
                using var ms = new MemoryStream();
                await client.DownloadFileAsync("bucket", "concurrent_read.txt", ms);
                var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                lock (lockObj)
                {
                    readResults.Add(content);
                }
            });
            readTasks.Add(task);
        }

        await Task.WhenAll(readTasks);

        // Assert - All reads should return the same content
        Assert.AreEqual(concurrentReads, readResults.Count);
        Assert.IsTrue(readResults.All(r => r == expectedContent));
    }

    [TestMethod]
    public async Task ConcurrentDeletes_SameFile_AllSucceed()
    {
        // Arrange
        var client = CreateLocalClient();
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        var filePath = Path.Combine(bucketPath, "to_delete.txt");
        await File.WriteAllTextAsync(filePath, "delete me");

        const int concurrentDeletes = 5;
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < concurrentDeletes; i++)
        {
            tasks.Add(client.DeleteFileAsync("bucket", "to_delete.txt"));
        }

        await Task.WhenAll(tasks);

        // Assert - All should succeed (idempotent)
        Assert.IsTrue(tasks.All(t => t.Status == TaskStatus.RanToCompletion));
        Assert.IsFalse(File.Exists(filePath));
    }

    [TestMethod]
    public async Task ConcurrentDirectoryCreation_SamePath_AllSucceed()
    {
        // Arrange
        var client = CreateLocalClient();
        const int concurrentUploads = 10;
        var tasks = new List<Task>();

        // Act - All upload to same directory that doesn't exist
        for (int i = 0; i < concurrentUploads; i++)
        {
            var index = i;
            var task = Task.Run(async () =>
            {
                using var stream = new MemoryStream(new byte[10]);
                await client.UploadStreamAsync("bucket", stream, $@"new\directory\file_{index}.txt", "text/plain");
            });
            tasks.Add(task);
        }

        // Should not throw - Directory.CreateDirectory is idempotent
        await Task.WhenAll(tasks);

        // Assert
        var newDir = Path.Combine(_testRootPath, "bucket", "new", "directory");
        Assert.IsTrue(Directory.Exists(newDir));
        Assert.AreEqual(concurrentUploads, Directory.GetFiles(newDir).Length);
    }

    [TestMethod]
    public async Task ConcurrentMetadataReads_SameFile_AllSucceed()
    {
        // Arrange
        var client = CreateLocalClient();
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);
        await File.WriteAllTextAsync(Path.Combine(bucketPath, "metadata_test.txt"), "content");

        const int concurrentReads = 100;
        var results = new List<CloudFileMetadata>();
        var lockObj = new object();

        // Act
        var tasks = Enumerable.Range(0, concurrentReads)
            .Select(_ => Task.Run(async () =>
            {
                var metadata = await client.GetFileMetadataAsync("bucket", "metadata_test.txt");
                lock (lockObj)
                {
                    results.Add(metadata);
                }
            }));

        await Task.WhenAll(tasks);

        // Assert - All should return same metadata
        Assert.AreEqual(concurrentReads, results.Count);
        Assert.IsTrue(results.All(r => r.Name == "metadata_test.txt"));
        Assert.IsTrue(results.All(r => r.Size == results[0].Size));
    }

    [TestMethod]
    public async Task ConcurrentFileList_WithModifications_HandlesGracefully()
    {
        // Arrange
        var client = CreateLocalClient();
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        // Create initial files
        for (int i = 0; i < 10; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(bucketPath, $"file_{i}.txt"), "x");
        }

        var listTask = Task.Run(async () =>
        {
            var files = new List<CloudFileMetadata>();
            await foreach (var file in client.GetFileListAsync("bucket"))
            {
                files.Add(file);
                await Task.Delay(10); // Slow down enumeration
            }
            return files;
        });

        // Modify files while listing
        var modifyTask = Task.Run(async () =>
        {
            await Task.Delay(20);
            for (int i = 10; i < 15; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(bucketPath, $"file_{i}.txt"), "new");
            }
        });

        // Act
        await Task.WhenAll(listTask, modifyTask);

        // Assert - Should complete without exception
        // The list may or may not include new files depending on timing
        var files = await listTask;
        Assert.IsTrue(files.Count >= 10); // At least the original files
    }

    #endregion

    #region Cancellation Tests

    [TestMethod]
    public async Task GetFileListAsync_WithCancellation_StopsEnumeratingAndFreeResources()
    {
        // Arrange
        var client = CreateLocalClient();
        var bucketPath = Path.Combine(_testRootPath, "bucket");
        Directory.CreateDirectory(bucketPath);

        for (int i = 0; i < 100; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(bucketPath, $"file_{i}.txt"), "x");
        }

        var cts = new CancellationTokenSource();
        var enumeratedCount = 0;

        // Act
        try
        {
            await foreach (var file in client.GetFileListAsync("bucket", null, cts.Token))
            {
                enumeratedCount++;
                if (enumeratedCount == 10)
                {
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.AreEqual(10, enumeratedCount);
        // Verify no resource leaks - difficult to test directly, but this is the pattern
    }

    [TestMethod]
    public async Task UploadStreamAsync_WhenCancelled_StopsAndCleanUp()
    {
        // Arrange
        var client = CreateLocalClient();
        var cts = new CancellationTokenSource();

        // Create a slow stream that allows cancellation
        using var slowStream = new SlowStream(new byte[1024 * 1024], delayPerRead: TimeSpan.FromMilliseconds(10));

        var uploadTask = Task.Run(async () =>
        {
            await client.UploadStreamAsync("bucket", slowStream, "large_file.bin", "application/octet-stream", cts.Token);
        });

        // Cancel after a short delay
        await Task.Delay(50);
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => uploadTask);
    }

    #endregion

    #region Helper Methods

    private Client CreateLocalClient()
    {
        var mockLogger = new Mock<ILogger<Client>>();
        var options = Options.Create(new StorageServiceSettings { Path = _testRootPath });
        var stubMeterFactory = new Ruya.Services.CloudStorage.Tests.Common.StubMeterFactory();
        var stubTracing = new Ruya.Services.CloudStorage.Tests.Common.StubDistributedTracing();
        return new Client(mockLogger.Object, stubTracing, stubMeterFactory, options);
    }

    /// <summary>
    /// A stream wrapper that adds artificial delays to simulate slow I/O.
    /// </summary>
    private class SlowStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly TimeSpan _delayPerRead;

        public SlowStream(byte[] data, TimeSpan delayPerRead)
        {
            _inner = new MemoryStream(data);
            _delayPerRead = delayPerRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(_delayPerRead);
            return _inner.Read(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(_delayPerRead, cancellationToken);
            return await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
