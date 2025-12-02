using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;
using Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests;

/// <summary>
/// Performance benchmark tests comparing BulkInsert vs EF Core AddRange.
/// These tests are marked with [TestCategory("Performance")] for selective execution.
/// </summary>
[TestClass]
[TestCategory("Performance")]
public class PerformanceBenchmarkTests
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

    #region Benchmark Tests

    [TestMethod]
    // [DataRow(100)]
    // [DataRow(1_000)]
    [DataRow(10_000)]
    public async Task Benchmark_BulkInsert_Vs_EFCore_AddRange(int recordCount)
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(recordCount, 1);

        // Benchmark BulkInsert
        await _fixture.CleanTablesAsync();
        await SeedCategoriesAsync();

        await using var sp1 = _fixture.CreateServiceProvider();
        await using var context1 = sp1.GetRequiredService<TestDbContext>();
        var bulkOps = sp1.GetRequiredService<IBulkInsertOperations>();

        var bulkStopwatch = Stopwatch.StartNew();
        await bulkOps.BulkInsertAsync(context1, products);
        bulkStopwatch.Stop();
        var bulkTime = bulkStopwatch.ElapsedMilliseconds;

        // Benchmark EF Core AddRange
        await _fixture.CleanTablesAsync();
        await SeedCategoriesAsync();

        // Recreate products with new instances (previous ones may have IDs set)
        var efProducts = TestDataGenerator.CreateProducts(recordCount, 1);

        await using var context2 = _fixture.CreateDbContext();

        var efStopwatch = Stopwatch.StartNew();
        context2.Products.AddRange(efProducts);
        await context2.SaveChangesAsync();
        efStopwatch.Stop();
        var efTime = efStopwatch.ElapsedMilliseconds;

        // Output results
        Console.WriteLine($"Records: {recordCount:N0}");
        Console.WriteLine($"BulkInsert: {bulkTime}ms");
        Console.WriteLine($"EF AddRange: {efTime}ms");
        Console.WriteLine($"Speedup: {(double)efTime / bulkTime:F2}x");

        // Assert - BulkInsert should be faster for larger datasets
        if (recordCount >= 1000)
        {
            Assert.IsTrue(bulkTime < efTime,
                $"BulkInsert ({bulkTime}ms) should be faster than EF Core ({efTime}ms) for {recordCount:N0} records");
        }
    }

    [TestMethod]
    [DataRow(1000, 100)]
    [DataRow(1000, 500)]
    [DataRow(1000, 1000)]
    [DataRow(1000, 2000)]
    public async Task Benchmark_DifferentBatchSizes(int recordCount, int batchSize)
    {
        // Arrange
        var products = TestDataGenerator.CreateProducts(recordCount, 1);

        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var bulkOps = sp.GetRequiredService<IBulkInsertOperations>();

        // Act
        var stopwatch = Stopwatch.StartNew();
        await bulkOps.BulkInsertAsync(context, products, new BulkInsertOptions
        {
            BatchSize = batchSize
        });
        stopwatch.Stop();

        // Output
        Console.WriteLine($"Records: {recordCount:N0}, BatchSize: {batchSize}, Time: {stopwatch.ElapsedMilliseconds}ms");

        // Verify all inserted
        var count = await context.Products.CountAsync();
        Assert.AreEqual(recordCount, count);
    }

    [TestMethod]
    public async Task Benchmark_WithTableLock_VsWithout()
    {
        const int recordCount = 10_000;

        // Without TableLock
        await _fixture.CleanTablesAsync();
        await SeedCategoriesAsync();

        var products1 = TestDataGenerator.CreateProducts(recordCount, 1);
        await using var sp1 = _fixture.CreateServiceProvider();
        await using var context1 = sp1.GetRequiredService<TestDbContext>();
        var bulkOps1 = sp1.GetRequiredService<IBulkInsertOperations>();

        var withoutLockStopwatch = Stopwatch.StartNew();
        await bulkOps1.BulkInsertAsync(context1, products1, new BulkInsertOptions { TableLock = false });
        withoutLockStopwatch.Stop();

        // With TableLock
        await _fixture.CleanTablesAsync();
        await SeedCategoriesAsync();

        var products2 = TestDataGenerator.CreateProducts(recordCount, 1);
        await using var sp2 = _fixture.CreateServiceProvider();
        await using var context2 = sp2.GetRequiredService<TestDbContext>();
        var bulkOps2 = sp2.GetRequiredService<IBulkInsertOperations>();

        var withLockStopwatch = Stopwatch.StartNew();
        await bulkOps2.BulkInsertAsync(context2, products2, new BulkInsertOptions { TableLock = true });
        withLockStopwatch.Stop();

        // Output
        Console.WriteLine($"Without TableLock: {withoutLockStopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"With TableLock: {withLockStopwatch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task Benchmark_WithVsWithoutTriggers()
    {
        const int recordCount = 5_000;

        // With Triggers
        await _fixture.CleanTablesAsync();
        await SeedCategoriesAsync();

        var products1 = TestDataGenerator.CreateProducts(recordCount, 1);
        await using var sp1 = _fixture.CreateServiceProvider();
        await using var context1 = sp1.GetRequiredService<TestDbContext>();
        var bulkOps1 = sp1.GetRequiredService<IBulkInsertOperations>();

        var withTriggersStopwatch = Stopwatch.StartNew();
        await bulkOps1.BulkInsertAsync(context1, products1, new BulkInsertOptions { FireTriggers = true });
        withTriggersStopwatch.Stop();

        // Without Triggers
        await _fixture.CleanTablesAsync();
        await SeedCategoriesAsync();

        var products2 = TestDataGenerator.CreateProducts(recordCount, 1);
        await using var sp2 = _fixture.CreateServiceProvider();
        await using var context2 = sp2.GetRequiredService<TestDbContext>();
        var bulkOps2 = sp2.GetRequiredService<IBulkInsertOperations>();

        var withoutTriggersStopwatch = Stopwatch.StartNew();
        await bulkOps2.BulkInsertAsync(context2, products2, new BulkInsertOptions { FireTriggers = false });
        withoutTriggersStopwatch.Stop();

        // Output
        Console.WriteLine($"With Triggers: {withTriggersStopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Without Triggers: {withoutTriggersStopwatch.ElapsedMilliseconds}ms");
    }

    #endregion

    #region Memory Pressure Tests

    [TestMethod]
    public async Task Benchmark_MemoryUsage_LargeDataset()
    {
        const int recordCount = 50_000;

        var products = TestDataGenerator.CreateProducts(recordCount, 1);

        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var bulkOps = sp.GetRequiredService<IBulkInsertOperations>();

        // Force GC before test
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var memoryBefore = GC.GetTotalMemory(true);

        var stopwatch = Stopwatch.StartNew();
        await bulkOps.BulkInsertAsync(context, products, new BulkInsertOptions
        {
            BatchSize = 5000
        });
        stopwatch.Stop();

        var memoryAfter = GC.GetTotalMemory(false);
        var memoryUsed = memoryAfter - memoryBefore;

        Console.WriteLine($"Records: {recordCount:N0}");
        Console.WriteLine($"Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Memory used: {memoryUsed / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Throughput: {recordCount / (stopwatch.ElapsedMilliseconds / 1000.0):F0} records/sec");

        var count = await context.Products.CountAsync();
        Assert.AreEqual(recordCount, count);
    }

    #endregion

    #region Throughput Tests

    [TestMethod]
    [DataRow(1_000)]
    [DataRow(10_000)]
    [DataRow(50_000)]
    public async Task Benchmark_Throughput_RecordsPerSecond(int recordCount)
    {
        var products = TestDataGenerator.CreateProducts(recordCount, 1);

        await using var sp = _fixture.CreateServiceProvider();
        await using var context = sp.GetRequiredService<TestDbContext>();
        var bulkOps = sp.GetRequiredService<IBulkInsertOperations>();

        var stopwatch = Stopwatch.StartNew();
        await bulkOps.BulkInsertAsync(context, products);
        stopwatch.Stop();

        var throughput = recordCount / stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine($"Records: {recordCount:N0}");
        Console.WriteLine($"Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Throughput: {throughput:N0} records/sec");

        // Assert reasonable throughput (at least 1000 records/sec for containerized SQL)
        Assert.IsTrue(throughput > 1000,
            $"Throughput ({throughput:N0} records/sec) should be greater than 1000 records/sec");
    }

    #endregion
}
