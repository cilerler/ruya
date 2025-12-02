using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;
using Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests;

[TestClass]
public class DbContextExtensionsTests
{
    private static SqlServerFixture _fixture = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _fixture = new SqlServerFixture();
        await _fixture.InitializeAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await _fixture.CleanTablesAsync();
        await SeedCategoriesAsync();
    }

    private async Task SeedCategoriesAsync()
    {
        await using var context = _fixture.CreateDbContext();
        var categories = TestDataGenerator.CreateCategories(5);
        context.Categories.AddRange(categories);
        await context.SaveChangesAsync();
    }

    #region Extension Method Availability Tests

    [TestMethod]
    public async Task BulkInsertAsync_ExtensionMethod_IsAvailable()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = TestDataGenerator.CreateProducts(10, 1);

        // Act
        var result = await context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(10, result);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithTableAndColumns_ExtensionIsAvailable()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var dtos = TestDataGenerator.CreateProductImportDtos(10, 1);

        // Act
        var result = await context.BulkInsertAsync(
            dtos,
            "dbo.Products",
            ["Name", "Description", "Price", "Quantity", "IsActive", "CreatedAt", "CategoryId"]);

        // Assert
        Assert.AreEqual(10, result);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithOptions_ExtensionIsAvailable()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = TestDataGenerator.CreateProducts(10, 1);
        var options = new BulkInsertOptions { BatchSize = 5 };

        // Act
        var result = await context.BulkInsertAsync(products, options);

        // Assert
        Assert.AreEqual(10, result);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithConfigureAction_ExtensionIsAvailable()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = TestDataGenerator.CreateProducts(10, 1);

        // Act
        var result = await context.BulkInsertAsync(products, opt =>
        {
            opt.BatchSize = 5;
            opt.Timeout = 60;
        });

        // Assert
        Assert.AreEqual(10, result);
    }

    #endregion

    #region DI Not Registered Tests

    [TestMethod]
    public async Task BulkInsertAsync_WhenDINotRegistered_ThrowsInvalidOperationException()
    {
        // Arrange - Create context without DI registration
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(opt =>
            opt.UseSqlServer(_fixture.ConnectionString));

        await using var sp = services.BuildServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = TestDataGenerator.CreateProducts(10, 1);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.BulkInsertAsync(products));
        StringAssert.Contains(ex.Message, "IBulkInsertOperations is not registered");
    }

    #endregion

    #region Null Argument Tests

    [TestMethod]
    public async Task BulkInsertAsync_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        TestDbContext context = null!;
        var products = TestDataGenerator.CreateProducts(10, 1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => context.BulkInsertAsync(products));
    }

    [TestMethod]
    public async Task BulkInsertAsync_NullConfigureAction_ThrowsArgumentNullException()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = TestDataGenerator.CreateProducts(10, 1);
        Action<BulkInsertOptions> configureOptions = null!;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => context.BulkInsertAsync(products, configureOptions));
    }

    #endregion
}