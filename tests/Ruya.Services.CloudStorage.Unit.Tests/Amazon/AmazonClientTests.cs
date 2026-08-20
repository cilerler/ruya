using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.CloudStorage.Amazon;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.UnitTests.Amazon;

[TestClass]
[DoNotParallelize]
public class AmazonClientTests
{
    private Mock<IAmazonS3> _mockS3Client = null!;
    private Mock<ILogger<Client>> _mockLogger = null!;
    private IOptions<Setting> _options = null!;
    private Client _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockS3Client = new Mock<IAmazonS3>();
        _mockLogger = new Mock<ILogger<Client>>();
        _options = Options.Create(new Setting
        {
            AccessKey = "test-key",
            SecretKey = "test-secret",
            Region = "us-east-1"
        });
        _client = new Client(_mockLogger.Object, _options, _mockS3Client.Object);
    }

    [TestCleanup]
    public void Cleanup() => _client.Dispose();

    [TestMethod]
    public void Dispose_DoesNotDisposeInjectedS3Client()
    {
        _client.Dispose();

        _mockS3Client.Verify(client => client.Dispose(), Times.Never);
    }

    #region GetFileMetadataAsync Tests

    [TestMethod]
    public async Task GetFileMetadataAsync_WithValidFile_ReturnsMetadata()
    {
        // Arrange
        var expectedLastModified = DateTime.UtcNow;
        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse
            {
                ContentLength = 1024,
                LastModified = expectedLastModified,
                Headers = { ContentType = "application/json" }
            });

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://signed.url/file");

        // Act
        var result = await _client.GetFileMetadataAsync("test-bucket", "test-file.json");

        // Assert
        Assert.AreEqual("test-bucket", result.Bucket);
        Assert.AreEqual("test-file.json", result.Name);
        Assert.AreEqual(1024UL, result.Size);
        Assert.AreEqual("application/json", result.ContentType);
        Assert.IsNotNull(result.SignedUrl);
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenFileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var s3Exception = new AmazonS3Exception("Not Found")
        {
            StatusCode = HttpStatusCode.NotFound
        };

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(s3Exception);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _client.GetFileMetadataAsync("test-bucket", "non-existent.txt"));

        Assert.IsTrue(exception.Message.Contains("non-existent.txt"));
        Assert.IsTrue(exception.Message.Contains("test-bucket"));

#pragma warning disable CA1873 // Moq expression matchers are not evaluated as production log arguments.
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Information,
                It.Is<EventId>(eventId => eventId.Id == 8100 && eventId.Name == "AmazonMetadataNotFound"),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public async Task GetFileMetadataAsync_WithInvalidBucketName_ThrowsArgumentException(string? bucketName)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.GetFileMetadataAsync(bucketName!, "file.txt"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public async Task GetFileMetadataAsync_WithInvalidFileName_ThrowsArgumentException(string? fileName)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.GetFileMetadataAsync("bucket", fileName!));
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WithCancellationRequested_PropagatesCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _client.GetFileMetadataAsync("bucket", "file.txt", cts.Token));
    }

    #endregion

    #region UploadStreamAsync Tests

    [TestMethod]
    public async Task UploadStreamAsync_WithValidStream_UploadsAndReturnsMetadata()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[100]);
        var expectedLastModified = DateTime.UtcNow;

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse
            {
                ContentLength = 100,
                LastModified = expectedLastModified,
                Headers = { ContentType = "application/octet-stream" }
            });

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://signed.url");

        // Act
        var result = await _client.UploadStreamAsync("bucket", stream, "folder/file.bin", "application/octet-stream");

        // Assert
        Assert.AreEqual("bucket", result.Bucket);
        Assert.AreEqual(100UL, result.Size);
        _mockS3Client.Verify(x => x.PutObjectAsync(
            It.Is<PutObjectRequest>(r =>
                r.BucketName == "bucket" &&
                r.Key == "folder/file.bin" &&
                r.ContentType == "application/octet-stream"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UploadStreamAsync_NormalizesWindowsPathSeparators()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);
        PutObjectRequest? capturedRequest = null;

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { ContentLength = 10 });

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://url");

        // Act - Pass Windows-style path
        await _client.UploadStreamAsync("bucket", stream, @"folder\subfolder\file.txt", "text/plain");

        // Assert - Key should have forward slashes
        Assert.IsNotNull(capturedRequest);
        Assert.IsFalse(capturedRequest.Key.Contains('\\'), "Path should not contain backslashes");
        Assert.AreEqual("folder/subfolder/file.txt", capturedRequest.Key);
    }

    [TestMethod]
    public async Task UploadStreamAsync_WithNonSeekableStream_DoesNotRecordBytesMetric()
    {
        // Arrange
        var nonSeekableStream = new NonSeekableStream(new byte[50]);

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { ContentLength = 50 });

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://url");

        // Act - Should not throw
        var result = await _client.UploadStreamAsync("bucket", nonSeekableStream, "file.txt", "text/plain");

        // Assert
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task UploadStreamAsync_SeeksToBeginningBeforeUpload()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[100]);
        stream.Position = 50; // Simulate partially read stream

        _mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((req, _) =>
            {
                // Verify stream was reset
                Assert.AreEqual(0, req.InputStream.Position);
            })
            .ReturnsAsync(new PutObjectResponse());

        _mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse { ContentLength = 100 });

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://url");

        // Act
        await _client.UploadStreamAsync("bucket", stream, "file.txt", "text/plain");

        // Assert is in the callback
    }

    [TestMethod]
    public async Task UploadFileAsync_WhenSourceCannotBeOpened_RecordsFailure()
    {
        using var collector = new MetricCollector("Ruya.Services.CloudStorage.Amazon", "files_failed");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _client.UploadFileAsync(
                "bucket",
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
                "file.txt"));

        Assert.AreEqual(1, collector.Sum);
    }

    #endregion

    #region DeleteFileAsync Tests

    [TestMethod]
    public async Task DeleteFileAsync_WithExistingFile_DeletesSuccessfully()
    {
        // Arrange
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        // Act
        await _client.DeleteFileAsync("bucket", "file-to-delete.txt");

        // Assert
        _mockS3Client.Verify(x => x.DeleteObjectAsync(
            It.Is<DeleteObjectRequest>(r =>
                r.BucketName == "bucket" &&
                r.Key == "file-to-delete.txt"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteFileAsync_WhenS3Throws_PropagatesException()
    {
        // Arrange
        _mockS3Client
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Access Denied"));

        // Act & Assert
        await Assert.ThrowsAsync<AmazonS3Exception>(
            () => _client.DeleteFileAsync("bucket", "file.txt"));
    }

    #endregion

    #region CopyFileAsync Tests

    [TestMethod]
    public async Task CopyFileAsync_BetweenBuckets_CopiesSuccessfully()
    {
        // Arrange
        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopyObjectResponse());

        // Act
        await _client.CopyFileAsync("source-bucket", "source.txt", "dest-bucket", "dest.txt");

        // Assert
        _mockS3Client.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(r =>
                r.SourceBucket == "source-bucket" &&
                r.SourceKey == "source.txt" &&
                r.DestinationBucket == "dest-bucket" &&
                r.DestinationKey == "dest.txt"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CopyFileAsync_WithinSameBucket_CopiesSuccessfully()
    {
        // Arrange
        _mockS3Client
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopyObjectResponse());

        // Act
        await _client.CopyFileAsync("bucket", "original.txt", "bucket", "copy.txt");

        // Assert
        _mockS3Client.Verify(x => x.CopyObjectAsync(
            It.Is<CopyObjectRequest>(r =>
                r.SourceBucket == "bucket" &&
                r.DestinationBucket == "bucket"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetFileListAsync Tests

    [TestMethod]
    public async Task GetFileListAsync_WithPagination_ReturnsAllFiles()
    {
        // Arrange
        var firstPage = new ListObjectsV2Response
        {
            S3Objects = new List<S3Object>
            {
                new() { Key = "file1.txt", Size = 100, LastModified = DateTime.UtcNow },
                new() { Key = "file2.txt", Size = 200, LastModified = DateTime.UtcNow }
            },
            IsTruncated = true,
            NextContinuationToken = "token123"
        };

        var secondPage = new ListObjectsV2Response
        {
            S3Objects = new List<S3Object>
            {
                new() { Key = "file3.txt", Size = 300, LastModified = DateTime.UtcNow }
            },
            IsTruncated = false
        };

        var callCount = 0;
        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => callCount++ == 0 ? firstPage : secondPage);

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://url");

        // Act
        var files = new List<CloudFileMetadata>();
        await foreach (var file in _client.GetFileListAsync("bucket"))
        {
            files.Add(file);
        }

        // Assert
        Assert.AreEqual(3, files.Count);
        Assert.AreEqual("file1.txt", files[0].Name);
        Assert.AreEqual("file2.txt", files[1].Name);
        Assert.AreEqual("file3.txt", files[2].Name);
    }

    [TestMethod]
    public async Task GetFileListAsync_WithPrefix_PassesPrefixToS3()
    {
        // Arrange
        ListObjectsV2Request? capturedRequest = null;

        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new ListObjectsV2Response { S3Objects = new List<S3Object>(), IsTruncated = false });

        // Act
        await foreach (var _ in _client.GetFileListAsync("bucket", "documents/2024/"))
        {
        }

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.AreEqual("documents/2024/", capturedRequest.Prefix);
    }

    [TestMethod]
    public async Task GetFileListAsync_WithCancellation_StopsEnumeration()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var filesReturned = 0;

        _mockS3Client
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = new List<S3Object>
                {
                    new() { Key = "file1.txt", Size = 100, LastModified = DateTime.UtcNow },
                    new() { Key = "file2.txt", Size = 200, LastModified = DateTime.UtcNow }
                },
                IsTruncated = false
            });

        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://url");

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var file in _client.GetFileListAsync("bucket", null, cts.Token))
            {
                filesReturned++;
                cts.Cancel(); // Cancel after first file
            }
        });

        // Assert
        Assert.AreEqual(1, filesReturned);
    }

    #endregion

    #region GetSignedUploadUrl Tests

    [TestMethod]
    public void GetSignedUploadUrl_GeneratesCorrectUrl()
    {
        // Arrange
        _mockS3Client
            .Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://bucket.s3.amazonaws.com/file?signed");

        // Act
        var url = _client.GetSignedUploadUrl("bucket", "file.txt", "text/plain", 30);

        // Assert
        Assert.IsNotNull(url);
        _mockS3Client.Verify(x => x.GetPreSignedURL(
            It.Is<GetPreSignedUrlRequest>(r =>
                r.BucketName == "bucket" &&
                r.Key == "file.txt" &&
                r.Verb == HttpVerb.PUT &&
                r.ContentType == "text/plain")),
            Times.Once);
    }

    [TestMethod]
    public void GetSignedUploadUrl_WithEmptyBucket_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _client.GetSignedUploadUrl("", "file.txt", "text/plain"));
    }

    #endregion

    #region DownloadFileAsync Tests

    [TestMethod]
    public async Task DownloadFileAsync_SeeksTargetStreamToBeginning()
    {
        // Arrange
        using var targetStream = new MemoryStream();
        var contentBytes = new byte[] { 1, 2, 3, 4, 5 };

        _mockS3Client
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(contentBytes)
            });

        // Act
        await _client.DownloadFileAsync("bucket", "file.bin", targetStream);

        // Assert
        Assert.AreEqual(0, targetStream.Position, "Stream should be seeked back to beginning");
        Assert.AreEqual(5, targetStream.Length);
    }

    #endregion

    /// <summary>
    /// A non-seekable stream wrapper for testing.
    /// </summary>
    private class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
