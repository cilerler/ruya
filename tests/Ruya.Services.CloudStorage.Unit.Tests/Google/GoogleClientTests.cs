using System;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Api.Gax;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Google;
using Ruya.Services.CloudStorage.UnitTests;
using GoogleObject = Google.Apis.Storage.v1.Data.Object;
using GoogleObjects = Google.Apis.Storage.v1.Data.Objects;

namespace Ruya.Services.CloudStorage.Google.Integration.Tests;

/// <summary>
/// Unit tests for Google Cloud Storage client.
/// Unit tests for configuration, path handling, and client ownership behavior.
/// </summary>
[TestClass]
public class GoogleClientTests
{
    #region Setting Tests

    [TestMethod]
    public void Setting_ProviderName_IsGoogle()
    {
        Assert.AreEqual("Google", StorageServiceSettings.ProviderName);
    }

    [TestMethod]
    public void Setting_ConfigurationSectionName_IsCloudStorageGoogle()
    {
        Assert.AreEqual("CloudStorage:Google", StorageServiceSettings.ConfigurationSectionName);
    }

    #endregion

    #region Path Normalization Tests

    [TestMethod]
    public void PathNormalization_WindowsPathShouldBeConvertedToForwardSlashes()
    {
        var windowsPath = @"folder\subfolder\file.txt";
        var expectedCloudPath = "folder/subfolder/file.txt";

        var destinationFileName = PathNormalizer.ToCloudPath(windowsPath);

        // Assert
        Assert.AreEqual(expectedCloudPath, destinationFileName);
    }

    [TestMethod]
    public void PathNormalization_AlreadyForwardSlashes_RemainsUnchanged()
    {
        var cloudPath = "folder/subfolder/file.txt";

        var destinationFileName = PathNormalizer.ToCloudPath(cloudPath);

        Assert.AreEqual(cloudPath, destinationFileName);
    }

    [TestMethod]
    public void PathNormalization_FileInRoot_NoLeadingSlash()
    {
        var rootFile = "file.txt";

        var destinationFileName = PathNormalizer.ToCloudPath(rootFile);

        Assert.AreEqual(rootFile, destinationFileName);
    }

    #endregion

    #region Logging Tests

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenObjectIsMissing_EmitsStableNotFoundEventId()
    {
        Mock<StorageClient> storageClient = CreateStorageClientMock();
        var apiException = new GoogleApiException("storage", "not found")
        {
            HttpStatusCode = HttpStatusCode.NotFound
        };
        storageClient
            .Setup(client => client.GetObjectAsync(
                "bucket",
                "missing.txt",
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(apiException);
        var logger = new Mock<ILogger<Client>>();
        using var meter = new Meter($"{nameof(GoogleClientTests)}.Logging");
        using Client client = CreateClient(
            storageClient.Object,
            meter,
            ownsStorageClient: false,
            logger.Object);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            client.GetFileMetadataAsync("bucket", "missing.txt"));

#pragma warning disable CA1873 // Moq expression matchers are not evaluated as production log arguments.
        logger.Verify(
            candidate => candidate.Log(
                LogLevel.Information,
                It.Is<EventId>(eventId => eventId.Id == 8300 && eventId.Name == "GoogleMetadataNotFound"),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenNonGoogleFailureOccurs_RecordsAndLogsFailure()
    {
        Mock<StorageClient> storageClient = CreateStorageClientMock();
        storageClient
            .Setup(client => client.GetObjectAsync(
                "bucket",
                "file.txt",
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("client failure"));
        var logger = new Mock<ILogger<Client>>();
        string meterName = $"{nameof(GoogleClientTests)}.MetadataFailure.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        using Client client = CreateClient(storageClient.Object, meter, ownsStorageClient: false, logger.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetFileMetadataAsync("bucket", "file.txt"));

        Assert.AreEqual(1, collector.Sum);
#pragma warning disable CA1873 // Moq expression matchers are not evaluated as production log arguments.
        logger.Verify(
            candidate => candidate.Log(
                LogLevel.Error,
                It.Is<EventId>(eventId => eventId.Id == 8301 && eventId.Name == "GoogleMetadataFailed"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    [TestMethod]
    public async Task GetFileMetadataAsync_WhenCallerCancels_DoesNotRecordFailure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Mock<StorageClient> storageClient = CreateStorageClientMock();
        storageClient
            .Setup(client => client.GetObjectAsync(
                "bucket",
                "file.txt",
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        string meterName = $"{nameof(GoogleClientTests)}.MetadataCancellation.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        using Client client = CreateClient(storageClient.Object, meter, ownsStorageClient: false);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.GetFileMetadataAsync("bucket", "file.txt", cancellation.Token));

        Assert.AreEqual(0, collector.Sum);
    }

    [TestMethod]
    public async Task GetFileListAsync_WhenEnumerationFails_RecordsFailure()
    {
        Mock<StorageClient> storageClient = CreateStorageClientMock();
        var fileList = new Mock<PagedAsyncEnumerable<GoogleObjects, GoogleObject>>();
        fileList
            .Setup(list => list.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(() => new ThrowingAsyncEnumerator<GoogleObject>(new InvalidOperationException("enumeration failed")));
        storageClient
            .Setup(client => client.ListObjectsAsync("bucket", null, It.IsAny<ListObjectsOptions>()))
            .Returns(fileList.Object);
        string meterName = $"{nameof(GoogleClientTests)}.ListFailure.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        using Client client = CreateClient(storageClient.Object, meter, ownsStorageClient: false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (CloudFileMetadata _ in client.GetFileListAsync("bucket"))
            {
                Assert.Fail("The failing enumerator must not yield an item.");
            }
        });

        Assert.AreEqual(1, collector.Sum);
    }

    [TestMethod]
    public async Task UploadFileAsync_WhenSourceCannotBeOpened_RecordsFailure()
    {
        Mock<StorageClient> storageClient = CreateStorageClientMock();
        string meterName = $"{nameof(GoogleClientTests)}.UploadFileFailure.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        using var collector = new MetricCollector(meterName, "files_failed");
        using Client client = CreateClient(storageClient.Object, meter, ownsStorageClient: false);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            client.UploadFileAsync("bucket", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"), "file.txt"));

        Assert.AreEqual(1, collector.Sum);
    }

    #endregion

    #region Ownership Tests

    [TestMethod]
    public void Dispose_WhenStorageClientIsInjected_DoesNotDisposeIt()
    {
        Mock<StorageClient> storageClient = CreateStorageClientMock();
        using var meter = new Meter($"{nameof(GoogleClientTests)}.Injected");
        var client = CreateClient(storageClient.Object, meter, ownsStorageClient: false);

        client.Dispose();

        storageClient.Verify(candidate => candidate.Dispose(), Times.Never);
    }

    [TestMethod]
    public void Dispose_WhenStorageClientIsOwned_DisposesItExactlyOnce()
    {
        Mock<StorageClient> storageClient = CreateStorageClientMock();
        using var meter = new Meter($"{nameof(GoogleClientTests)}.Owned");
        var client = CreateClient(storageClient.Object, meter, ownsStorageClient: true);

        client.Dispose();
        client.Dispose();

        storageClient.Verify(candidate => candidate.Dispose(), Times.Once);
    }

    [TestMethod]
    public void Constructor_WhenOwnedClientInitializationFails_DisposesStorageClient()
    {
        Mock<StorageClient> storageClient = CreateStorageClientMock();
        var logger = new Mock<ILogger<Client>>();
        var tracing = new Mock<IDistributedTracing>();
        var meterFactory = new Mock<IMeterFactory>();
        meterFactory
            .Setup(factory => factory.Create(It.IsAny<MeterOptions>()))
            .Throws(new InvalidOperationException("meter creation failed"));
        UrlSigner signer = UrlSigner.FromBlobSigner(Mock.Of<UrlSigner.IBlobSigner>());
        IOptions<StorageServiceSettings> options = Options.Create(new StorageServiceSettings
        {
            Credential = "unused-by-injected-clients"
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => new Client(
            logger.Object,
            tracing.Object,
            meterFactory.Object,
            options,
            storageClient.Object,
            signer,
            ownsStorageClient: true));

        storageClient.Verify(candidate => candidate.Dispose(), Times.Once);
    }

    private static Mock<StorageClient> CreateStorageClientMock() => new();

    private static Client CreateClient(
        StorageClient storageClient,
        Meter meter,
        bool ownsStorageClient,
        ILogger<Client>? logger = null)
    {
        logger ??= Mock.Of<ILogger<Client>>();
        var tracing = new Mock<IDistributedTracing>();
        var meterFactory = new Mock<IMeterFactory>();
        meterFactory
            .Setup(factory => factory.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);
        UrlSigner signer = UrlSigner.FromBlobSigner(Mock.Of<UrlSigner.IBlobSigner>());
        IOptions<StorageServiceSettings> options = Options.Create(new StorageServiceSettings
        {
            Credential = "unused-by-injected-clients"
        });

        return new Client(
            logger,
            tracing.Object,
            meterFactory.Object,
            options,
            storageClient,
            signer,
            ownsStorageClient);
    }

    #endregion
}
