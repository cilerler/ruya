using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.CloudStorage.Azure;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.UnitTests.Azure;

[TestClass]
[DoNotParallelize]
public class AzureClientTests
{
    private Mock<BlobServiceClient> _mockServiceClient = null!;
    private Mock<BlobContainerClient> _mockContainerClient = null!;
    private Mock<BlobClient> _mockBlobClient = null!;
    private Mock<ILogger<Client>> _mockLogger = null!;
    private Client _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<Client>>();
        _mockServiceClient = new Mock<BlobServiceClient>();
        _mockContainerClient = new Mock<BlobContainerClient>();
        _mockBlobClient = new Mock<BlobClient>();

        // Default setup for container client
        _mockServiceClient
            .Setup(x => x.GetBlobContainerClient(It.IsAny<string>()))
            .Returns(_mockContainerClient.Object);

        _mockContainerClient
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(_mockBlobClient.Object);

        _mockContainerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContainerInfo>(), Mock.Of<Response>()));

        _client = new Client(_mockLogger.Object, _mockServiceClient.Object);
    }

    #region Container Caching Tests

    [TestMethod]
    public async Task GetFileMetadataAsync_DoesNotCreateContainerAsReadSideEffect()
    {
        // Arrange
        SetupBlobPropertiesResponse();

        // Act - Call twice for the same bucket
        await _client.GetFileMetadataAsync("test-container", "file1.txt");
        await _client.GetFileMetadataAsync("test-container", "file2.txt");

        // Assert - read operations must not create infrastructure
        _mockContainerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadStreamAsync_CachesContainerCreationPerContainer()
    {
        using var first = new MemoryStream(new byte[10]);
        using var second = new MemoryStream(new byte[10]);
        _mockBlobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));
        SetupBlobPropertiesResponse();

        await _client.UploadStreamAsync("container-a", first, "file-a.txt", "text/plain");
        await _client.UploadStreamAsync("container-a", second, "file-b.txt", "text/plain");

        _mockContainerClient.Verify(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobContainerEncryptionScopeOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetFileMetadataAsync Tests

    [TestMethod]
    public async Task GetFileMetadataAsync_WithExistingBlob_ReturnsMetadata()
    {
        // Arrange
        var lastModified = DateTimeOffset.UtcNow;
        SetupBlobPropertiesResponse(contentLength: 2048, contentType: "application/pdf", lastModified: lastModified);

        // Act
        var result = await _client.GetFileMetadataAsync("container", "document.pdf");

        // Assert
        Assert.AreEqual("container", result.Bucket);
        Assert.AreEqual("document.pdf", result.Name);
        Assert.AreEqual(2048UL, result.Size);
        Assert.AreEqual("application/pdf", result.ContentType);
        Assert.AreEqual(lastModified.UtcDateTime, result.LastModified);
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenBlobNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var exception = new RequestFailedException((int)HttpStatusCode.NotFound, "Blob not found");

        _mockBlobClient
            .Setup(x => x.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _client.GetFileMetadataAsync("container", "missing.txt"));

        Assert.IsTrue(ex.Message.Contains("missing.txt"));
        Assert.IsTrue(ex.Message.Contains("container"));

#pragma warning disable CA1873 // Moq expression matchers are not evaluated as production log arguments.
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Information,
                It.Is<EventId>(eventId => eventId.Id == 8200 && eventId.Name == "AzureMetadataNotFound"),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenOtherAzureError_PropagatesException()
    {
        // Arrange
        var exception = new RequestFailedException((int)HttpStatusCode.Forbidden, "Access denied");

        _mockBlobClient
            .Setup(x => x.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        await Assert.ThrowsAsync<RequestFailedException>(
            () => _client.GetFileMetadataAsync("container", "file.txt"));
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenContainerClientSetupFails_RecordsFailure()
    {
        _mockServiceClient
            .Setup(service => service.GetBlobContainerClient("container"))
            .Throws(new InvalidOperationException("container setup failed"));
        using var collector = new MetricCollector("Ruya.Services.CloudStorage.Azure", "files_failed");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _client.GetFileMetadataAsync("container", "file.txt"));

        Assert.AreEqual(1, collector.Sum);
    }

    #endregion

    #region UploadStreamAsync Tests

    [TestMethod]
    public async Task UploadStreamAsync_NormalizesPathSeparators()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);
        string? capturedBlobName = null;

        var mockSpecificBlobClient = new Mock<BlobClient>();
        _mockContainerClient
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Callback<string>(name => capturedBlobName = name)
            .Returns(mockSpecificBlobClient.Object);

        mockSpecificBlobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));

        SetupBlobPropertiesResponse(mockBlobClient: mockSpecificBlobClient);

        // Act
        await _client.UploadStreamAsync("container", stream, @"folder\subfolder\file.txt", "text/plain");

        // Assert
        Assert.IsNotNull(capturedBlobName);
        Assert.IsFalse(capturedBlobName.Contains('\\'));
        Assert.AreEqual("folder/subfolder/file.txt", capturedBlobName);
    }

    [TestMethod]
    public async Task UploadStreamAsync_SeeksStreamToBeginning()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[100]);
        stream.Position = 50;

        Stream? capturedStream = null;
        _mockBlobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, BlobUploadOptions, CancellationToken>((s, _, _) => capturedStream = s)
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));

        SetupBlobPropertiesResponse();

        // Act
        await _client.UploadStreamAsync("container", stream, "file.txt", "text/plain");

        // Assert
        Assert.AreEqual(0, capturedStream?.Position ?? -1);
    }

    [TestMethod]
    public async Task UploadStreamAsync_SetsCorrectContentType()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);
        BlobUploadOptions? capturedOptions = null;

        _mockBlobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, BlobUploadOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));

        SetupBlobPropertiesResponse();

        // Act
        await _client.UploadStreamAsync("container", stream, "file.json", "application/json");

        // Assert
        Assert.IsNotNull(capturedOptions?.HttpHeaders);
        Assert.AreEqual("application/json", capturedOptions.HttpHeaders.ContentType);
    }

    [TestMethod]
    public async Task UploadFileAsync_WhenSourceCannotBeOpened_RecordsFailure()
    {
        using var collector = new MetricCollector("Ruya.Services.CloudStorage.Azure", "files_failed");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _client.UploadFileAsync(
                "container",
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
                "file.txt"));

        Assert.AreEqual(1, collector.Sum);
    }

    #endregion

    #region GetFileListAsync Tests

    [TestMethod]
    public async Task GetFileListAsync_WhenEnumerationFails_RecordsFailure()
    {
        var blobs = new Mock<AsyncPageable<BlobItem>>();
        blobs
            .Setup(pageable => pageable.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(() => new ThrowingAsyncEnumerator<BlobItem>(new InvalidOperationException("enumeration failed")));
        _mockContainerClient
            .Setup(container => container.GetBlobsAsync(
                It.IsAny<BlobTraits>(),
                It.IsAny<BlobStates>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(blobs.Object);
        using var collector = new MetricCollector("Ruya.Services.CloudStorage.Azure", "files_failed");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (CloudFileMetadata _ in _client.GetFileListAsync("container"))
            {
                Assert.Fail("The failing enumerator must not yield an item.");
            }
        });

        Assert.AreEqual(1, collector.Sum);
    }

    [TestMethod]
    public async Task GetFileListAsync_WhenCallerCancels_DoesNotRecordFailure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var blobs = new Mock<AsyncPageable<BlobItem>>();
        blobs
            .Setup(pageable => pageable.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(() => new ThrowingAsyncEnumerator<BlobItem>(new OperationCanceledException(cancellation.Token)));
        _mockContainerClient
            .Setup(container => container.GetBlobsAsync(
                It.IsAny<BlobTraits>(),
                It.IsAny<BlobStates>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(blobs.Object);
        using var collector = new MetricCollector("Ruya.Services.CloudStorage.Azure", "files_failed");

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (CloudFileMetadata _ in _client.GetFileListAsync("container", cancellationToken: cancellation.Token))
            {
                Assert.Fail("The canceled enumerator must not yield an item.");
            }
        });

        Assert.AreEqual(0, collector.Sum);
    }

    #endregion

    #region CopyFileAsync Tests

    [TestMethod]
    public async Task CopyFileAsync_WhenSourceNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        _mockBlobClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _client.CopyFileAsync("source-container", "missing.txt", "dest-container", "copy.txt"));
    }

    [TestMethod]
    public async Task CopyFileAsync_WithExistingSource_PerformsCopy()
    {
        // Arrange
        var mockSourceBlob = new Mock<BlobClient>();
        var mockDestBlob = new Mock<BlobClient>();
        var mockCopyOperation = new Mock<CopyFromUriOperation>();

        _mockContainerClient
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns((string name) => name == "source.txt" ? mockSourceBlob.Object : mockDestBlob.Object);

        mockSourceBlob
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        mockSourceBlob
            .Setup(x => x.CanGenerateSasUri)
            .Returns(true);

        mockSourceBlob
            .Setup(x => x.GenerateSasUri(It.IsAny<global::Azure.Storage.Sas.BlobSasBuilder>()))
            .Returns(new Uri("https://storage.blob.core.windows.net/container/blob?sas"));

        mockSourceBlob.Setup(x => x.BlobContainerName).Returns("source-container");
        mockSourceBlob.Setup(x => x.Name).Returns("source.txt");

        mockDestBlob
            .Setup(x => x.StartCopyFromUriAsync(
                It.IsAny<Uri>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<AccessTier?>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<RehydratePriority?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockCopyOperation.Object);

        mockCopyOperation
            .Setup(x => x.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<long>>());

        // Act
        await _client.CopyFileAsync("source-container", "source.txt", "dest-container", "dest.txt");

        // Assert
        mockDestBlob.Verify(x => x.StartCopyFromUriAsync(
            It.IsAny<Uri>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<AccessTier?>(),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<RehydratePriority?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteFileAsync Tests

    [TestMethod]
    public async Task DeleteFileAsync_UsesDeleteIfExists()
    {
        // Arrange
        _mockBlobClient
            .Setup(x => x.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        // Act
        await _client.DeleteFileAsync("container", "file.txt");

        // Assert
        _mockBlobClient.Verify(x => x.DeleteIfExistsAsync(
            It.IsAny<DeleteSnapshotsOption>(),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteFileAsync_WhenBlobDoesNotExist_DoesNotThrow()
    {
        // Arrange
        _mockBlobClient
            .Setup(x => x.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>())); // false = didn't exist

        // Act & Assert - Should not throw
        await _client.DeleteFileAsync("container", "non-existent.txt");
    }

    [TestMethod]
    public async Task DeleteFileAsync_WhenContainerDoesNotExist_DoesNotThrow()
    {
        // Arrange
        _mockBlobClient
            .Setup(x => x.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException((int)HttpStatusCode.NotFound, "Container not found"));

        // Act & Assert - Delete remains idempotent when the container is absent.
        await _client.DeleteFileAsync("missing-container", "non-existent.txt");
    }

    #endregion

    #region GetSignedUploadUrl Tests

    [TestMethod]
    public void GetSignedUploadUrl_WhenCannotGenerateSas_ReturnsEmpty()
    {
        // Arrange
        _mockBlobClient
            .Setup(x => x.CanGenerateSasUri)
            .Returns(false);

        // Act
        var url = _client.GetSignedUploadUrl("container", "file.txt", "text/plain");

        // Assert
        Assert.AreEqual(string.Empty, url);
    }

    #endregion

    #region Helper Methods

    private void SetupBlobPropertiesResponse(
        long contentLength = 1024,
        string contentType = "application/octet-stream",
        DateTimeOffset? lastModified = null,
        Mock<BlobClient>? mockBlobClient = null)
    {
        var client = mockBlobClient ?? _mockBlobClient;
        var mockProps = BlobsModelFactory.BlobProperties(
            contentLength: contentLength,
            contentType: contentType,
            lastModified: lastModified ?? DateTimeOffset.UtcNow);

        client
            .Setup(x => x.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(mockProps, Mock.Of<Response>()));

        client
            .Setup(x => x.CanGenerateSasUri)
            .Returns(true);

        client
            .Setup(x => x.GenerateSasUri(It.IsAny<global::Azure.Storage.Sas.BlobSasBuilder>()))
            .Returns(new Uri("https://storage.blob.core.windows.net/container/blob?sas"));

        client.Setup(x => x.BlobContainerName).Returns("container");
        client.Setup(x => x.Name).Returns("file.txt");
    }

    #endregion
}
