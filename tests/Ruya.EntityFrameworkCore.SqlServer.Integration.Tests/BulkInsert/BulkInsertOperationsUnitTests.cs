using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.EntityFrameworkCore.ModelMetadata;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;
using Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

namespace Ruya.EntityFrameworkCore.BulkInsert.Tests;

[TestClass]
[TestCategory("Unit")]
public class BulkInsertOperationsUnitTests
{
    private Mock<ILogger<BulkInsertOperations>> _loggerMock = null!;
    private Mock<IDistributedTracing> _tracingMock = null!;
    private Mock<IOptions<BulkInsertOperationsSettings>> _optionsMock = null!;
    private Mock<IModelMetadata> _modelMetadataMock = null!;
    private BulkInsertOperations _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<BulkInsertOperations>>();
        _tracingMock = new Mock<IDistributedTracing>();
        _optionsMock = new Mock<IOptions<BulkInsertOperationsSettings>>();
        _modelMetadataMock = new Mock<IModelMetadata>();

        _tracingMock
            .Setup(t => t.StartActivity(
                It.IsAny<string>(),
                It.IsAny<ActivityKind>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<IEnumerable<KeyValuePair<string, object?>>?>()))
            .Returns(ActivityScope.Empty);

        _optionsMock
            .Setup(o => o.Value)
            .Returns(new BulkInsertOperationsSettings());

        _sut = new BulkInsertOperations(_loggerMock.Object, _tracingMock.Object, _optionsMock.Object, _modelMetadataMock.Object);
    }

    #region Constructor and Properties Tests

    [TestMethod]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange & Act
        var instance = new BulkInsertOperations(_loggerMock.Object, _tracingMock.Object, _optionsMock.Object, _modelMetadataMock.Object);

        // Assert
        Assert.IsNotNull(instance);
    }

    [TestMethod]
    public void Constructor_SetsTimeoutFromOptions()
    {
        // Arrange
        _optionsMock.Setup(o => o.Value).Returns(new BulkInsertOperationsSettings { Timeout = 120 });

        // Act
        var instance = new BulkInsertOperations(_loggerMock.Object, _tracingMock.Object, _optionsMock.Object, _modelMetadataMock.Object);

        // Assert
        Assert.AreEqual(120, instance.Timeout);
    }

    [TestMethod]
    public void Constructor_SetsBatchSizeFromOptions()
    {
        // Arrange
        _optionsMock.Setup(o => o.Value).Returns(new BulkInsertOperationsSettings { BatchSize = 5000 });

        // Act
        var instance = new BulkInsertOperations(_loggerMock.Object, _tracingMock.Object, _optionsMock.Object, _modelMetadataMock.Object);

        // Assert
        Assert.AreEqual(5000, instance.BatchSize);
    }

    [TestMethod]
    public void Timeout_DefaultValue_Is30()
    {
        // Assert
        Assert.AreEqual(30, _sut.Timeout);
    }

    [TestMethod]
    public void BatchSize_DefaultValue_Is1000()
    {
        // Assert
        Assert.AreEqual(1000, _sut.BatchSize);
    }

    #endregion

    #region Argument Validation Tests

    [TestMethod]
    public async Task BulkInsertAsync_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var entities = new List<Product>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.BulkInsertAsync<Product>(null!, entities));
        Assert.AreEqual("context", ex.ParamName);
    }

    [TestMethod]
    public async Task BulkInsertAsync_NullEntities_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test")
            .Options;
        await using var context = new TestDbContext(options);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.BulkInsertAsync<Product>(context, null!));
        Assert.AreEqual("entities", ex.ParamName);
    }

    [TestMethod]
    public async Task BulkInsertAsync_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test")
            .Options;
        await using var context = new TestDbContext(options);
        var entities = new List<Product>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.BulkInsertAsync(context, entities, (BulkInsertOptions)null!));
        Assert.AreEqual("options", ex.ParamName);
    }

    #endregion

    #region Empty Collection Tests

    [TestMethod]
    public async Task BulkInsertAsync_EmptyCollection_ReturnsZero()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test_empty")
            .Options;
        await using var context = new TestDbContext(options);
        var entities = Array.Empty<Product>();

        // Act
        var result = await _sut.BulkInsertAsync(context, entities);

        // Assert
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task BulkInsertAsync_EmptyList_ReturnsZero()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test_empty_list")
            .Options;
        await using var context = new TestDbContext(options);
        var entities = new List<Product>();

        // Act
        var result = await _sut.BulkInsertAsync(context, entities);

        // Assert
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task BulkInsertAsync_EmptyCollection_StillCallsTracing()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test_empty_tracing")
            .Options;
        await using var context = new TestDbContext(options);
        var entities = Array.Empty<Product>();

        // Act
        await _sut.BulkInsertAsync(context, entities);

        // Assert
        _tracingMock.Verify(t => t.StartActivity(
            "BulkInsert",
            ActivityKind.Client,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<IEnumerable<KeyValuePair<string, object?>>?>()), Times.Once);
    }

    #endregion

    #region BulkInsertOptions Tests

    [TestMethod]
    public void BulkInsertOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new BulkInsertOptions();

        // Assert
        Assert.IsNull(options.TableName);
        Assert.IsNull(options.Columns);
        Assert.AreEqual(1000, options.BatchSize);
        Assert.AreEqual(30, options.Timeout);
        Assert.IsTrue(options.CheckConstraints);
        Assert.IsTrue(options.FireTriggers);
        Assert.IsFalse(options.KeepIdentity);
        Assert.IsFalse(options.KeepNulls);
        Assert.IsFalse(options.TableLock);
        Assert.IsNull(options.NotifyAfter);
    }

    [TestMethod]
    public void BulkInsertOptions_AllPropertiesCanBeSet()
    {
        // Arrange
        var notifyCallback = (long _) => { };

        // Act
        var options = new BulkInsertOptions
        {
            TableName = "dbo.TestTable",
            Columns = ["Col1", "Col2"],
            BatchSize = 5000,
            Timeout = 120,
            CheckConstraints = false,
            FireTriggers = false,
            KeepIdentity = true,
            KeepNulls = true,
            TableLock = true,
            NotifyAfter = notifyCallback
        };

        // Assert
        Assert.AreEqual("dbo.TestTable", options.TableName);
        CollectionAssert.AreEquivalent(new[] { "Col1", "Col2" }, options.Columns);
        Assert.AreEqual(5000, options.BatchSize);
        Assert.AreEqual(120, options.Timeout);
        Assert.IsFalse(options.CheckConstraints);
        Assert.IsFalse(options.FireTriggers);
        Assert.IsTrue(options.KeepIdentity);
        Assert.IsTrue(options.KeepNulls);
        Assert.IsTrue(options.TableLock);
        Assert.AreSame(notifyCallback, options.NotifyAfter);
    }

    #endregion

    #region Settings Tests

    [TestMethod]
    public void BulkInsertOperationsSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new BulkInsertOperationsSettings();

        // Assert
        Assert.AreEqual(30, settings.Timeout);
        Assert.AreEqual(1000, settings.BatchSize);
    }

    [TestMethod]
    public void BulkInsertOperationsSettings_ConfigurationSectionName_IsCorrect()
    {
        // Pin the released external configuration contract independently of its symbol-derived implementation.
        Assert.AreEqual("BulkInsertOperations", BulkInsertOperationsSettings.ConfigurationSectionName);
    }

    #endregion

    #region InMemory Provider Error Tests

    [TestMethod]
    public async Task BulkInsertAsync_InMemoryProvider_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test_inmemory")
            .Options;
        await using var context = new TestDbContext(options);
        var entities = TestDataGenerator.CreateProducts(10);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.BulkInsertAsync(context, entities));
    }

    #endregion

    #region Cancellation Tests

    [TestMethod]
    public async Task BulkInsertAsync_CancelledToken_DoesNotThrowForEmptyCollection()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test_cancelled")
            .Options;
        await using var context = new TestDbContext(options);
        var entities = Array.Empty<Product>();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await _sut.BulkInsertAsync(context, entities, cts.Token);

        // Assert
        Assert.AreEqual(0, result);
    }

    #endregion

    #region Tracing Tests

    [TestMethod]
    public async Task BulkInsertAsync_OnError_StillCallsStartActivity()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test_error_tracing")
            .Options;
        await using var context = new TestDbContext(options);
        var entities = TestDataGenerator.CreateProducts(10);

        // Act
        try
        {
            await _sut.BulkInsertAsync(context, entities);
        }
        catch
        {
            // Expected
        }

        // Assert - Activity lifecycle is managed by ActivityScope.Dispose()
        _tracingMock.Verify(t => t.StartActivity(
            "BulkInsert",
            ActivityKind.Client,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<IEnumerable<KeyValuePair<string, object?>>?>()), Times.Once);
    }

    #endregion

    #region ResolveDbColumnName Tests

    public class ColumnMappedTestEntity
    {
        public int Id { get; set; }

        [Column("OrgId")]
        public int OrganizationId { get; set; }

        [Column("DisplayName")]
        public string FullName { get; set; } = string.Empty;

        public string UnmappedProperty { get; set; } = string.Empty;
    }

    [TestMethod]
    public void ResolveDbColumnName_WithColumnAttribute_ReturnsAttributeName()
    {
        // Act
        var result = BulkInsertOperations.ResolveDbColumnName(
            typeof(ColumnMappedTestEntity),
            nameof(ColumnMappedTestEntity.OrganizationId));

        // Assert
        Assert.AreEqual("OrgId", result);
    }

    [TestMethod]
    public void ResolveDbColumnName_WithoutColumnAttribute_ReturnsPropertyName()
    {
        // Act
        var result = BulkInsertOperations.ResolveDbColumnName(
            typeof(ColumnMappedTestEntity),
            nameof(ColumnMappedTestEntity.Id));

        // Assert
        Assert.AreEqual(nameof(ColumnMappedTestEntity.Id), result);
    }

    [TestMethod]
    public void ResolveDbColumnName_WithDbColumnNameInput_PassesThrough()
    {
        // Act — "OrgId" is a DB column name, not a C# property name
        var result = BulkInsertOperations.ResolveDbColumnName(typeof(ColumnMappedTestEntity), "OrgId");

        // Assert
        Assert.AreEqual("OrgId", result);
    }

    #endregion
}
