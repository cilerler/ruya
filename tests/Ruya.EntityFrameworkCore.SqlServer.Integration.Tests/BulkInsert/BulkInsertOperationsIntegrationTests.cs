using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;
using Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests;

[TestClass]
public class BulkInsertOperationsIntegrationTests
{
    private static SqlServerFixture _fixture = null!;
    private ServiceProvider _serviceProvider = null!;
    private TestDbContext _context = null!;
    private IBulkInsertOperations _bulkOperations = null!;

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
        _serviceProvider = _fixture.CreateServiceProvider();
        _context = _serviceProvider.GetRequiredService<TestDbContext>();
        _bulkOperations = _serviceProvider.GetRequiredService<IBulkInsertOperations>();

        await _fixture.CleanTablesAsync();
        await SeedCategoriesAsync();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await _context.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }

    private async Task SeedCategoriesAsync()
    {
        await using var seedContext = _fixture.CreateDbContext();
        var categories = TestDataGenerator.CreateCategories(5);
        seedContext.Categories.AddRange(categories);
        await seedContext.SaveChangesAsync();
    }

    #region Basic Insert Tests

    [TestMethod]
    public async Task BulkInsertAsync_SingleEntity_InsertsSuccessfully()
    {
        // Arrange
        var products = new[] { TestDataGenerator.CreateProduct(1, 1) };

        // Act
        var result = await _context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(1, result);

        var inserted = await _context.Products.CountAsync();
        Assert.AreEqual(1, inserted);
    }

    [TestMethod]
    public async Task BulkInsertAsync_MultipleEntities_InsertsAllSuccessfully()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(100, 1);

        // Act
        var result = await _context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(100, result);

        var inserted = await _context.Products.CountAsync();
        Assert.AreEqual(100, inserted);
    }

    [TestMethod]
    public async Task BulkInsertAsync_LargeDataset_InsertsSuccessfully()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(10_000, 1);

        // Act
        var result = await _context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(10_000, result);

        var inserted = await _context.Products.CountAsync();
        Assert.AreEqual(10_000, inserted);
    }

    [TestMethod]
    public async Task BulkInsertAsync_EmptyCollection_ReturnsZeroWithoutError()
    {
        // Arrange
        var products = Array.Empty<Product>();

        // Act
        var result = await _context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(0, result);

        var inserted = await _context.Products.CountAsync();
        Assert.AreEqual(0, inserted);
    }

    #endregion

    #region Data Integrity Tests

    [TestMethod]
    public async Task BulkInsertAsync_PreservesAllPropertyValues()
    {
        // Arrange
        var product = new Product
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 99.99m,
            Quantity = 42,
            IsActive = true,
            CreatedAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            CategoryId = 1
        };

        // Act
        await _context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await _context.Products.FirstAsync();
        Assert.AreEqual("Test Product", inserted.Name);
        Assert.AreEqual("Test Description", inserted.Description);
        Assert.AreEqual(99.99m, inserted.Price);
        Assert.AreEqual(42, inserted.Quantity);
        Assert.IsTrue(inserted.IsActive);
        Assert.AreEqual(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc), inserted.CreatedAt);
        Assert.AreEqual(1, inserted.CategoryId);
    }

    [TestMethod]
    public async Task BulkInsertAsync_HandlesNullableProperties()
    {
        // Arrange
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Description = null;
        product.UpdatedAt = null;

        // Act
        await _context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await _context.Products.FirstAsync();
        Assert.IsNull(inserted.Description);
        Assert.IsNull(inserted.UpdatedAt);
    }

    [TestMethod]
    public async Task BulkInsertAsync_HandlesSpecialCharacters()
    {
        // Arrange
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Name = "Product with 'quotes' and \"double quotes\"";
        product.Description = "Description with émojis 🎉 and ünïcödé";

        // Act
        await _context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await _context.Products.FirstAsync();
        Assert.AreEqual("Product with 'quotes' and \"double quotes\"", inserted.Name);
        Assert.AreEqual("Description with émojis 🎉 and ünïcödé", inserted.Description);
    }

    [TestMethod]
    public async Task BulkInsertAsync_HandlesDecimalPrecision()
    {
        // Arrange
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Price = 12345.67m;

        // Act
        await _context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await _context.Products.FirstAsync();
        Assert.AreEqual(12345.67m, inserted.Price);
    }

    #endregion

    #region Transaction Tests

    [TestMethod]
    public async Task BulkInsertAsync_WithTransaction_CommitsSuccessfully()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(50, 1);
        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Act
        var result = await _context.BulkInsertAsync(products);
        await transaction.CommitAsync();

        // Assert
        Assert.AreEqual(50, result);

        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Products.CountAsync();
        Assert.AreEqual(50, count);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithTransaction_RollbackDiscardsData()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(50, 1);
        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Act
        await _context.BulkInsertAsync(products);
        await transaction.RollbackAsync();

        // Assert
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Products.CountAsync();
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task BulkInsertAsync_MultipleBulkInsertsInTransaction_AllOrNothing()
    {
        // Arrange
        var orders = Enumerable.Range(1, 10).Select(i => TestDataGenerator.CreateOrder(i)).ToArray();
        var products = TestDataGenerator.CreateProducts(100, 1);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Act
        await _context.BulkInsertAsync(orders);
        await _context.BulkInsertAsync(products);
        await transaction.CommitAsync();

        // Assert
        await using var verifyContext = _fixture.CreateDbContext();
        var orderCount = await verifyContext.Orders.CountAsync();
        var productCount = await verifyContext.Products.CountAsync();

        Assert.AreEqual(10, orderCount);
        Assert.AreEqual(100, productCount);
    }

    [TestMethod]
    public async Task BulkInsertAsync_TransactionRollbackOnError_AllDataDiscarded()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(50, 1);
        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Act
        await _context.BulkInsertAsync(products);

        // Simulate error condition - don't commit, just rollback
        await transaction.RollbackAsync();

        // Assert
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Products.CountAsync();
        Assert.AreEqual(0, count);
    }

    #endregion

    #region Options Tests

    [TestMethod]
    public async Task BulkInsertAsync_WithCustomBatchSize_InsertsSuccessfully()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(500, 1);

        // Act
        var result = await _context.BulkInsertAsync(products, opt =>
        {
            opt.BatchSize = 100;
        });

        // Assert
        Assert.AreEqual(500, result);

        var count = await _context.Products.CountAsync();
        Assert.AreEqual(500, count);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithTableLock_InsertsSuccessfully()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(1000, 1);

        // Act
        var result = await _context.BulkInsertAsync(products, opt =>
        {
            opt.TableLock = true;
        });

        // Assert
        Assert.AreEqual(1000, result);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithProgressCallback_ReportsProgress()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(500, 1);
        var progressReports = new List<long>();

        // Act
        await _context.BulkInsertAsync(products, opt =>
        {
            opt.BatchSize = 100;
            opt.NotifyAfter = rowsCopied => progressReports.Add(rowsCopied);
        });

        // Assert
        Assert.IsTrue(progressReports.Count > 0);
        CollectionAssert.AreEqual(progressReports.OrderBy(x => x).ToList(), progressReports);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithFireTriggersDisabled_InsertsSuccessfully()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(100, 1);

        // Act
        var result = await _context.BulkInsertAsync(products, opt =>
        {
            opt.FireTriggers = false;
        });

        // Assert
        Assert.AreEqual(100, result);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithExplicitTableAndColumns_InsertsSuccessfully()
    {
        // Arrange
        var dtos = TestDataGenerator.CreateProductImportDtos(100, 1);
        var columns = new[] { "Name", "Description", "Price", "Quantity", "IsActive", "CreatedAt", "CategoryId" };

        // Act
        var result = await _context.BulkInsertAsync(dtos, "dbo.Products", columns);

        // Assert
        Assert.AreEqual(100, result);

        var count = await _context.Products.CountAsync();
        Assert.AreEqual(100, count);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithCustomTimeout_DoesNotTimeout()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(1000, 1);

        // Act
        var result = await _context.BulkInsertAsync(products, opt =>
        {
            opt.Timeout = 300; // 5 minutes
        });

        // Assert
        Assert.AreEqual(1000, result);
    }

    #endregion

    #region Constraint Tests

    [TestMethod]
    public async Task BulkInsertAsync_WithCheckConstraints_EnforcesConstraints()
    {
        // Arrange - FK constraint: CategoryId must exist
        var products = new[]
        {
            new Product
            {
                Name = "Invalid Product",
                Price = 10.00m,
                Quantity = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CategoryId = 999 // Non-existent category
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
            await _context.BulkInsertAsync(products, opt =>
            {
                opt.CheckConstraints = true;
            }));
    }

    #endregion

    #region Cancellation Tests

    [TestMethod]
    public async Task BulkInsertAsync_CancellationRequested_StopsOperation()
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(10_000, 1);
        var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _context.BulkInsertAsync(products, opt =>
            {
                opt.BatchSize = 100;
                opt.NotifyAfter = _ => cts.Cancel(); // Cancel after first batch
            }, cts.Token));
    }

    #endregion

    #region Concurrent Insert Tests

    [TestMethod]
    public async Task BulkInsertAsync_ConcurrentInserts_AllSucceed()
    {
        // Arrange
        var tasks = new List<Task<long>>();

        // Act
        for (var i = 0; i < 5; i++)
        {
            var categoryId = i % 5 + 1;
            var products = TestDataGenerator.CreateProducts(100, categoryId)
                .Select((p, idx) => { p.Name = $"Batch{i}-{idx:D4}"; return p; })
                .ToArray();

            await using var context = _fixture.CreateDbContext();
            var sp = _fixture.CreateServiceProvider();
            var bulkOps = sp.GetRequiredService<IBulkInsertOperations>();

            tasks.Add(bulkOps.BulkInsertAsync(context, products));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.IsTrue(results.All(r => r == 100L));

        await using var verifyContext = _fixture.CreateDbContext();
        var totalCount = await verifyContext.Products.CountAsync();
        Assert.AreEqual(500, totalCount);
    }

    #endregion

    #region Column Attribute Mapping Tests

    [TestMethod]
    public async Task BulkInsertAsync_WithColumnAttribute_MapsNameofToDbColumnName()
    {
        // Arrange — use nameof() for column names, just like darkseid does with NormalizedVendor
        var entities = new[]
        {
            new ColumnMappedProduct { OrganizationId = 42, FullName = "Acme Corp", Price = 99.99m, Quantity = 10 },
            new ColumnMappedProduct { OrganizationId = 7, FullName = "Globex Inc", Price = 50.00m, Quantity = 5 }
        };

        var columns = new[]
        {
            nameof(ColumnMappedProduct.OrganizationId), // [Column("OrgId")] — C# name differs from DB
            nameof(ColumnMappedProduct.FullName),        // [Column("DisplayName")] — C# name differs from DB
            nameof(ColumnMappedProduct.Price),           // [Column(TypeName=...)] only — name unchanged
            nameof(ColumnMappedProduct.Quantity)          // no [Column] — pass-through
        };

        // Act
        var result = await _bulkOperations.BulkInsertAsync(_context, entities, "dbo.ColumnMappedProducts", columns);

        // Assert — data landed in the correct DB columns
        Assert.AreEqual(2, result);

        var inserted = await _context.ColumnMappedProducts.OrderBy(p => p.OrganizationId).ToListAsync();
        Assert.AreEqual(2, inserted.Count);

        Assert.AreEqual(7, inserted[0].OrganizationId);
        Assert.AreEqual("Globex Inc", inserted[0].FullName);
        Assert.AreEqual(50.00m, inserted[0].Price);
        Assert.AreEqual(5, inserted[0].Quantity);

        Assert.AreEqual(42, inserted[1].OrganizationId);
        Assert.AreEqual("Acme Corp", inserted[1].FullName);
        Assert.AreEqual(99.99m, inserted[1].Price);
        Assert.AreEqual(10, inserted[1].Quantity);
    }

    [TestMethod]
    public async Task BulkInsertAsync_WithColumnAttribute_AutoDetectedColumns_MapsCorrectly()
    {
        // Arrange — let EF Core auto-detect columns (no explicit column list)
        var entities = new[]
        {
            new ColumnMappedProduct { OrganizationId = 100, FullName = "Auto Detect Co", Price = 25.50m, Quantity = 3 }
        };

        // Act — auto-detect path: GetEntityMetadata resolves columns from EF model
        var result = await _context.BulkInsertAsync(entities);

        // Assert
        Assert.AreEqual(1, result);

        var inserted = await _context.ColumnMappedProducts.SingleAsync();
        Assert.AreEqual(100, inserted.OrganizationId);
        Assert.AreEqual("Auto Detect Co", inserted.FullName);
        Assert.AreEqual(25.50m, inserted.Price);
        Assert.AreEqual(3, inserted.Quantity);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public async Task BulkInsertAsync_IEnumerableNotMaterialized_InsertsSuccessfully()
    {
        // Arrange - Use LINQ query that's not materialized
        var products = Enumerable.Range(1, 100)
            .Select(i => TestDataGenerator.CreateProduct(i, 1));

        // Act
        var result = await _context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(100, result);
    }

    [TestMethod]
    public async Task BulkInsertAsync_MaxLengthStrings_InsertsWithoutTruncation()
    {
        // Arrange
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Name = new string('A', 200); // Max length
        product.Description = new string('B', 1000); // Max length

        // Act
        await _context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await _context.Products.FirstAsync();
        Assert.AreEqual(200, inserted.Name.Length);
        Assert.AreEqual(1000, inserted.Description!.Length);
    }

    [TestMethod]
    public async Task BulkInsertAsync_DateTimeEdgeCases_PreservesValues()
    {
        // Arrange
        var products = new[]
        {
            new Product { Name = "MinDate", Price = 1, Quantity = 1, IsActive = true,
                CreatedAt = new DateTime(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc), CategoryId = 1 },
            new Product { Name = "MaxDate", Price = 1, Quantity = 1, IsActive = true,
                CreatedAt = new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc), CategoryId = 1 }
        };

        // Act
        await _context.BulkInsertAsync(products);

        // Assert
        var inserted = await _context.Products.OrderBy(p => p.CreatedAt).ToListAsync();
        Assert.AreEqual(1753, inserted[0].CreatedAt.Year);
        Assert.AreEqual(9999, inserted[1].CreatedAt.Year);
    }

    #endregion
}
