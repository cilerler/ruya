using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;
using Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests;

[TestClass]
public class EdgeCaseAndErrorTests
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

    #region Foreign Key Constraint Tests

    [TestMethod]
    public async Task BulkInsert_InvalidForeignKey_ThrowsSqlException()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = new[]
        {
            new Product
            {
                Name = "Test",
                Price = 10,
                Quantity = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CategoryId = 9999 // Non-existent
            }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SqlException>(() => context.BulkInsertAsync(products));
        Assert.IsTrue(ex.Message.Contains("FOREIGN KEY") || ex.Message.Contains("FK_"));
    }

    #endregion

    #region Data Type Boundary Tests

    [TestMethod]
    public async Task BulkInsert_MaxDecimalValue_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Price = 9999999999999999.99m; // Max for decimal(18,2)

        // Act
        await context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await context.Products.FirstAsync();
        Assert.AreEqual(9999999999999999.99m, inserted.Price);
    }

    [TestMethod]
    public async Task BulkInsert_MinDecimalValue_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Price = -9999999999999999.99m;

        // Act
        await context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await context.Products.FirstAsync();
        Assert.AreEqual(-9999999999999999.99m, inserted.Price);
    }

    [TestMethod]
    public async Task BulkInsert_ZeroValues_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Price = 0;
        product.Quantity = 0;

        // Act
        await context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await context.Products.FirstAsync();
        Assert.AreEqual(0m, inserted.Price);
        Assert.AreEqual(0, inserted.Quantity);
    }

    [TestMethod]
    public async Task BulkInsert_NegativeQuantity_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Quantity = -100;

        // Act
        await context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await context.Products.FirstAsync();
        Assert.AreEqual(-100, inserted.Quantity);
    }

    #endregion

    #region String Edge Cases

    [TestMethod]
    public async Task BulkInsert_EmptyString_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Name = "Valid"; // Name is required
        product.Description = string.Empty;

        // Act
        await context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await context.Products.FirstAsync();
        Assert.AreEqual(string.Empty, inserted.Description);
    }

    [TestMethod]
    public async Task BulkInsert_StringTooLong_ThrowsException()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Name = new string('X', 201); // Max is 200

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => context.BulkInsertAsync(new[] { product }));
    }

    [TestMethod]
    public async Task BulkInsert_WhitespaceString_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Name = "   Valid Name   ";
        product.Description = "   ";

        // Act
        await context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await context.Products.FirstAsync();
        Assert.AreEqual("   Valid Name   ", inserted.Name);
        Assert.AreEqual("   ", inserted.Description);
    }

    [TestMethod]
    public async Task BulkInsert_SqlInjectionAttempt_SafelyInserted()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var product = TestDataGenerator.CreateProduct(1, 1);
        product.Name = "'; DROP TABLE Products; --";
        product.Description = "SELECT * FROM Users WHERE 1=1";

        // Act
        await context.BulkInsertAsync(new[] { product });

        // Assert
        var inserted = await context.Products.FirstAsync();
        Assert.AreEqual("'; DROP TABLE Products; --", inserted.Name);

        // Verify table still exists
        var count = await context.Products.CountAsync();
        Assert.AreEqual(1, count);
    }

    #endregion

    #region Collection Type Tests

    [TestMethod]
    public async Task BulkInsert_Array_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        Product[] products = TestDataGenerator.CreateProducts(10, 1);

        // Act
        var result = await context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(10L, result);
    }

    [TestMethod]
    public async Task BulkInsert_List_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        List<Product> products = TestDataGenerator.CreateProducts(10, 1).ToList();

        // Act
        var result = await context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(10L, result);
    }

    [TestMethod]
    public async Task BulkInsert_HashSet_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = new HashSet<Product>(TestDataGenerator.CreateProducts(10, 1));

        // Act
        var result = await context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(10L, result);
    }

    [TestMethod]
    public async Task BulkInsert_LinkedList_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = new LinkedList<Product>(TestDataGenerator.CreateProducts(10, 1));

        // Act
        var result = await context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(10L, result);
    }

    [TestMethod]
    public async Task BulkInsert_LazyEnumerable_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        // Lazy enumerable (not materialized)
        var products = Enumerable.Range(1, 100)
            .Select(i => TestDataGenerator.CreateProduct(i, 1));

        // Act
        var result = await context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(100L, result);
    }

    #endregion

    #region Connection State Tests

    [TestMethod]
    public async Task BulkInsert_ConnectionAlreadyOpen_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = TestDataGenerator.CreateProducts(10, 1);

        // Open connection before bulk insert
        await context.Database.OpenConnectionAsync();

        // Act
        var result = await context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(10L, result);

        // Verify connection is still open
        Assert.AreEqual(System.Data.ConnectionState.Open, context.Database.GetDbConnection().State);
    }

    #endregion

    #region Duplicate Data Tests

    [TestMethod]
    public async Task BulkInsert_DuplicateDataInBatch_AllInserted()
    {
        // Arrange - Products with same data but different objects
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = Enumerable.Range(1, 10)
            .Select(_ => new Product
            {
                Name = "Same Name",
                Price = 10.00m,
                Quantity = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CategoryId = 1
            })
            .ToArray();

        // Act
        var result = await context.BulkInsertAsync(products);

        // Assert
        Assert.AreEqual(10L, result);
        var count = await context.Products.CountAsync();
        Assert.AreEqual(10, count);
    }

    #endregion

    #region Mixed Boolean Tests

    [TestMethod]
    public async Task BulkInsert_MixedBooleanValues_Succeeds()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = new[]
        {
            new Product { Name = "Active", Price = 1, Quantity = 1, IsActive = true, CreatedAt = DateTime.UtcNow, CategoryId = 1 },
            new Product { Name = "Inactive", Price = 1, Quantity = 1, IsActive = false, CreatedAt = DateTime.UtcNow, CategoryId = 1 }
        };

        // Act
        await context.BulkInsertAsync(products);

        // Assert
        var activeCount = await context.Products.CountAsync(p => p.IsActive);
        var inactiveCount = await context.Products.CountAsync(p => !p.IsActive);

        Assert.AreEqual(1, activeCount);
        Assert.AreEqual(1, inactiveCount);
    }

    #endregion

    #region Schema Detection Tests

    [TestMethod]
    public async Task BulkInsert_AutoDetectsSchemaFromDbContext()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var categories = TestDataGenerator.CreateCategories(5)
            .Select((c, i) => new Category { Name = $"AutoDetect-{i}" })
            .ToArray();

        // Clear existing categories first
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Categories");

        // Act - No explicit table name provided, should auto-detect
        var result = await context.BulkInsertAsync(categories);

        // Assert
        Assert.AreEqual(5L, result);
        var count = await context.Categories.CountAsync();
        Assert.AreEqual(5, count);
    }

    #endregion

    #region Concurrent Access Tests

    [TestMethod]
    public async Task BulkInsert_MultipleConcurrentThreads_AllSucceedWithoutDataCorruption()
    {
        // Arrange - Multiple threads calling BulkInsertAsync simultaneously
        const int threadCount = 10;
        const int rowsPerThread = 100;
        var tasks = new List<Task<long>>();
        var barrier = new Barrier(threadCount); // Ensure all threads start at same time

        // Act
        for (var i = 0; i < threadCount; i++)
        {
            var threadIndex = i;
            var task = Task.Run(async () =>
            {
                barrier.SignalAndWait(); // Synchronize start

                await using var sp = _fixture.CreateServiceProvider();
                await using var context = sp.GetRequiredService<TestDbContext>();
                var bulkOps = sp.GetRequiredService<IBulkInsertOperations>();

                var products = Enumerable.Range(1, rowsPerThread)
                    .Select(j => new Product
                    {
                        Name = $"Thread{threadIndex}-Product{j:D4}",
                        Price = threadIndex * 100 + j,
                        Quantity = j,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        CategoryId = (threadIndex % 5) + 1
                    })
                    .ToArray();

                return await bulkOps.BulkInsertAsync(context, products);
            });
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All threads should report success
        Assert.IsTrue(results.All(r => r == rowsPerThread));

        // Verify total row count
        await using var verifyContext = _fixture.CreateDbContext();
        var totalCount = await verifyContext.Products.CountAsync();
        Assert.AreEqual(threadCount * rowsPerThread, totalCount);

        // Verify no data corruption - each thread's data should be intact
        for (var i = 0; i < threadCount; i++)
        {
            var threadPrefix = $"Thread{i}-";
            var threadRowCount = await verifyContext.Products
                .CountAsync(p => p.Name.StartsWith(threadPrefix));
            Assert.AreEqual(rowsPerThread, threadRowCount, $"Thread {i} should have {rowsPerThread} rows");
        }
    }

    [TestMethod]
    public async Task BulkInsert_ConcurrentInsertsToSameTable_NoDeadlocks()
    {
        // Arrange - Stress test for deadlock detection
        const int iterations = 20;
        var tasks = new List<Task>();

        // Act - Fire many concurrent inserts without waiting
        for (var i = 0; i < iterations; i++)
        {
            var batchIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                await using var sp = _fixture.CreateServiceProvider();
                await using var context = sp.GetRequiredService<TestDbContext>();

                var products = Enumerable.Range(1, 50)
                    .Select(j => new Product
                    {
                        Name = $"Deadlock-Test-{batchIndex}-{j}",
                        Price = j,
                        Quantity = 1,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        CategoryId = 1
                    })
                    .ToArray();

                await context.BulkInsertAsync(products);
            }));
        }

        // Should complete without deadlock
        await Task.WhenAll(tasks);

        // Verify all data was inserted
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Products.CountAsync();
        Assert.AreEqual(iterations * 50, count);
    }

    #endregion

    #region Cancellation During Bulk Copy Tests

    [TestMethod]
    public async Task BulkInsert_CancellationDuringLargeInsert_ThrowsOperationCancelled()
    {
        // Arrange - Large dataset that takes time to insert
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        // Generate large dataset lazily to ensure cancellation happens during insert
        var cts = new CancellationTokenSource();
        var rowsGenerated = 0;

        IEnumerable<Product> GenerateProducts()
        {
            for (var i = 1; i <= 50_000; i++)
            {
                Interlocked.Increment(ref rowsGenerated);
                // Cancel after generating some rows
                if (rowsGenerated == 1000)
                {
                    cts.Cancel();
                }
                yield return new Product
                {
                    Name = $"Cancellation-Test-{i}",
                    Price = i,
                    Quantity = 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CategoryId = 1
                };
            }
        }

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => context.BulkInsertAsync(GenerateProducts(), cts.Token));
    }

    [TestMethod]
    public async Task BulkInsert_PreCancelledToken_ThrowsImmediately()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var products = TestDataGenerator.CreateProducts(100, 1);
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        // Act & Assert - Should throw before doing any work (during connection open)
        await Assert.ThrowsAsync<OperationCanceledException>(() => context.BulkInsertAsync(products, cts.Token));
    }

    [TestMethod]
    public async Task BulkInsert_CancellationWithSmallBatchSize_CancelsPromptly()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        var progressReports = new List<long>();
        var cts = new CancellationTokenSource();

        var products = TestDataGenerator.CreateProducts(10_000, 1);

        // Act - Cancel after first progress report
        await Assert.ThrowsAsync<OperationCanceledException>(() => context.BulkInsertAsync(products, opt =>
        {
            opt.BatchSize = 100;
            opt.NotifyAfterRows = 100; // Use separate notification cadence
            opt.NotifyAfter = rowsCopied =>
            {
                progressReports.Add(rowsCopied);
                if (progressReports.Count >= 2) // Cancel after second batch
                {
                    cts.Cancel();
                }
            };
        }, cts.Token));

        // Assert - Should have received some progress reports before cancellation
        Assert.IsTrue(progressReports.Count >= 1);
    }

    #endregion

    #region Column Name Mismatch Tests

    [TestMethod]
    public async Task BulkInsert_MissingColumn_ThrowsException()
    {
        // Arrange - DTO missing a required column that exists in table
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        var dtos = new[]
        {
            new { Name = "Test", Price = 10.0m } // Missing Quantity, IsActive, etc.
        };

        // Act - Try to insert with incomplete column list
        // Assert - Should fail due to NOT NULL constraint on Quantity, etc.
        await Assert.ThrowsAsync<SqlException>(() => context.BulkInsertAsync(
            dtos,
            "dbo.Products",
            new[] { "Name", "Price" })); // Missing required columns
    }

    [TestMethod]
    public async Task BulkInsert_InvalidColumnName_ThrowsException()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        var dtos = TestDataGenerator.CreateProductImportDtos(10, 1);

        // Act & Assert - Use column name that doesn't exist in table
        var ex = await Assert.ThrowsAsync<Exception>(() => context.BulkInsertAsync(
            dtos,
            "dbo.Products",
            new[] { "Name", "NonExistentColumn", "Price" }));
        Assert.IsTrue(ex.Message.Contains("NonExistentColumn") ||
                      ex.Message.Contains("column") ||
                      ex is InvalidOperationException || ex is SqlException);
    }

    [TestMethod]
    public async Task BulkInsert_PropertyNameMismatchWithColumnMapping_ThrowsException()
    {
        // Arrange - DTO with property names that don't match column names
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        var dtos = new[]
        {
            new { ProductName = "Test", ProductPrice = 10.0m, Qty = 1 }
        };

        // Act & Assert - Columns refer to DTO properties that don't match DB columns
        await Assert.ThrowsAsync<Exception>(() => context.BulkInsertAsync(
            dtos,
            "dbo.Products",
            new[] { "ProductName", "ProductPrice", "Qty" })); // These don't exist as DB columns
    }

    [TestMethod]
    public async Task BulkInsert_ExtraColumnsInDto_OnlyMapsSpecifiedColumns()
    {
        // Arrange - DTO has extra properties not in column list
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        var dtos = Enumerable.Range(1, 10).Select(i => new
        {
            Name = $"Product-{i}",
            Description = $"Desc-{i}",
            Price = i * 10.0m,
            Quantity = i,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CategoryId = 1,
            ExtraProperty = "Should be ignored",
            AnotherExtra = 999
        }).ToArray();

        // Act - Only map required columns, ignore extras
        var result = await context.BulkInsertAsync(
            dtos,
            "dbo.Products",
            new[] { "Name", "Description", "Price", "Quantity", "IsActive", "CreatedAt", "CategoryId" });

        // Assert
        Assert.AreEqual(10L, result);
        var inserted = await context.Products.ToListAsync();
        Assert.AreEqual(10, inserted.Count);
    }

    #endregion

    #region NotifyAfterRows Tests

    [TestMethod]
    public async Task BulkInsert_NotifyAfterRows_ReportsAtSpecifiedInterval()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        var products = TestDataGenerator.CreateProducts(1000, 1);
        var progressReports = new List<long>();

        // Act - Use NotifyAfterRows different from BatchSize
        await context.BulkInsertAsync(products, opt =>
        {
            opt.BatchSize = 500; // Write in batches of 500
            opt.NotifyAfterRows = 100; // Report every 100 rows
            opt.NotifyAfter = rowsCopied => progressReports.Add(rowsCopied);
        });

        // Assert - Should get more frequent progress reports than batches
        Assert.IsTrue(progressReports.Count > 1);
        // Reports should be at ~100 row intervals (100, 200, 300, etc.)
        for (var i = 1; i < progressReports.Count; i++)
        {
            Assert.IsTrue(progressReports[i] >= progressReports[i - 1], "Progress reports should be in ascending order");
        }
    }

    [TestMethod]
    public async Task BulkInsert_NotifyAfterRows_DefaultsToBatchSizeWhenNotSet()
    {
        // Arrange
        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();

        var products = TestDataGenerator.CreateProducts(500, 1);
        var progressReports = new List<long>();

        // Act - Don't set NotifyAfterRows, should default to BatchSize
        await context.BulkInsertAsync(products, opt =>
        {
            opt.BatchSize = 100;
            // NotifyAfterRows not set - should use BatchSize
            opt.NotifyAfter = rowsCopied => progressReports.Add(rowsCopied);
        });

        // Assert - Should get reports at batch size intervals
        Assert.IsTrue(progressReports.Count > 0);
        // First report should be around BatchSize (100)
        Assert.AreEqual(100L, progressReports[0]);
    }

    #endregion
}