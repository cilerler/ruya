using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.EntityFrameworkCore.SqlServer;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;
using Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests.BulkInsert;

[TestClass]
public class StartupExtensionsTests
{
    private static IServiceCollection CreateServicesWithTracing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();

        // Add configuration with required BulkInsertOperations section
        var configData = new Dictionary<string, string?>
        {
            { "BulkInsertOperations:BatchSize", "1000" },
            { "BulkInsertOperations:Timeout", "30" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        var tracingMock = new Mock<IDistributedTracing>();
        tracingMock
            .Setup(t => t.StartActivity(
                It.IsAny<string>(),
                It.IsAny<ActivityKind>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<IEnumerable<KeyValuePair<string, object?>>?>()))
            .Returns(ActivityScope.Empty);

        services.AddSingleton(tracingMock.Object);
        return services;
    }

    #region AddBulkInsertOperations Basic Tests

    [TestMethod]
    public void AddBulkInsertOperations_RegistersIBulkInsertOperations()
    {
        // Arrange
        var services = CreateServicesWithTracing();
        services.AddDbContext<TestDbContext>();

        // Act
        services.AddBulkInsertOperations<TestDbContext>();
        using var sp = services.BuildServiceProvider();
        var bulkOps = sp.GetService<IBulkInsertOperations>();

        // Assert
        Assert.IsNotNull(bulkOps);
        Assert.IsInstanceOfType<BulkInsertOperations>(bulkOps);
    }

    [TestMethod]
    public void AddBulkInsertOperations_RegistersAsSingleton()
    {
        // Arrange
        var services = CreateServicesWithTracing();
        services.AddDbContext<TestDbContext>();
        services.AddBulkInsertOperations<TestDbContext>();

        // Act
        using var sp = services.BuildServiceProvider();
        var instance1 = sp.GetRequiredService<IBulkInsertOperations>();
        var instance2 = sp.GetRequiredService<IBulkInsertOperations>();

        // Assert
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void AddBulkInsertOperations_DoesNotOverrideExistingRegistration()
    {
        // Arrange
        var services = CreateServicesWithTracing();
        services.AddDbContext<TestDbContext>();
        var mockBulkOps = new Mock<IBulkInsertOperations>();
        services.AddSingleton(mockBulkOps.Object);

        // Act
        services.AddBulkInsertOperations<TestDbContext>();
        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IBulkInsertOperations>();

        // Assert
        Assert.AreSame(mockBulkOps.Object, resolved);
    }

    [TestMethod]
    public void AddBulkInsertOperations_NullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => services.AddBulkInsertOperations<TestDbContext>());
        Assert.AreEqual("serviceCollection", ex.ParamName);
    }

    [TestMethod]
    public void AddBulkInsertOperations_ReturnsServiceCollection_ForChaining()
    {
        // Arrange
        var services = CreateServicesWithTracing();
        services.AddDbContext<TestDbContext>();

        // Act
        var result = services.AddBulkInsertOperations<TestDbContext>();

        // Assert
        Assert.AreSame(services, result);
    }

    [TestMethod]
    public void AddBulkInsertOperations_WithoutIDistributedTracing_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDbContext<TestDbContext>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => services.AddBulkInsertOperations<TestDbContext>());
        StringAssert.Contains(ex.Message, "IDistributedTracing");
    }

    #endregion

    #region Configuration Tests

    [TestMethod]
    public void AddBulkInsertOperations_UsesDefaultSettings()
    {
        // Arrange
        var services = CreateServicesWithTracing();
        services.AddDbContext<TestDbContext>();

        // Act
        services.AddBulkInsertOperations<TestDbContext>();
        using var sp = services.BuildServiceProvider();
        var bulkOps = sp.GetRequiredService<IBulkInsertOperations>();

        // Assert
        Assert.AreEqual(30, bulkOps.Timeout);
        Assert.AreEqual(1000, bulkOps.BatchSize);
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
    public void BulkInsertOperationsSettings_CanSetAllProperties()
    {
        // Arrange & Act
        var settings = new BulkInsertOperationsSettings
        {
            Timeout = 600,
            BatchSize = 25000
        };

        // Assert
        Assert.AreEqual(600, settings.Timeout);
        Assert.AreEqual(25000, settings.BatchSize);
    }

    #endregion
}