using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.EntityFrameworkCore.SqlServer;
using Testcontainers.MsSql;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

/// <summary>
/// Shared test fixture that manages SQL Server container lifecycle.
/// </summary>
public sealed class SqlServerFixture : IAsyncDisposable
{
    private const string DatabaseName = "RuyaEntityFrameworkCoreTests";
    private const string CreateDatabaseSql = """
        IF DB_ID(N'RuyaEntityFrameworkCoreTests') IS NULL
            CREATE DATABASE [RuyaEntityFrameworkCoreTests];

        ALTER DATABASE [RuyaEntityFrameworkCoreTests]
            SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
        """;

    private readonly MsSqlContainer _container;
    private bool _isInitialized;

    public SqlServerFixture()
    {
        _container = new MsSqlBuilder()
            .WithImage("cilerler/mssql-server-linux:2025-RTM-ubuntu-22.04")
            .WithPassword("YourStrong!Passw0rd")
            .Build();
    }

    public string ConnectionString => new SqlConnectionStringBuilder(_container.GetConnectionString())
    {
        InitialCatalog = DatabaseName
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _container.StartAsync();
        await CreateDatabaseAsync();
        await CreateDatabaseSchemaAsync();
        _isInitialized = true;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task CreateDatabaseSchemaAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = CreateDatabaseSql;
        await command.ExecuteNonQueryAsync();
    }

    public TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new TestDbContext(options);
    }

    public ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

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

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Register required dependency
        services.AddSingleton<IDistributedTracing, NoOpDistributedTracing>();

        services.AddBulkInsertOperations<TestDbContext>();

        // Register DbContext with UseApplicationServiceProvider to expose IBulkInsertOperations
        // to the DbContext's internal service provider
        services.AddDbContext<TestDbContext>((sp, options) =>
            options.UseSqlServer(ConnectionString)
                   .UseApplicationServiceProvider(sp));

        return services.BuildServiceProvider();
    }

    public async Task CleanTablesAsync()
    {
        await using var context = CreateDbContext();

        // Delete in correct order due to FK constraints
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.BatchItems");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.ColumnMappedProducts");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.OrderItems");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Orders");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Products");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Categories");

        // Reset identity seeds
        await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('dbo.BatchItems', RESEED, 0)");
        await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('dbo.ColumnMappedProducts', RESEED, 0)");
        await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('dbo.OrderItems', RESEED, 0)");
        await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('dbo.Orders', RESEED, 0)");
        await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('dbo.Products', RESEED, 0)");
        await context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('dbo.Categories', RESEED, 0)");
    }

    private sealed class NoOpDistributedTracing : IDistributedTracing
    {
        public ActivityScope StartActivity(
            string activityName,
            ActivityKind activityKind = ActivityKind.Internal,
            string? parentId = null,
            string? cacheKey = null,
            IEnumerable<KeyValuePair<string, object?>>? tags = null) => ActivityScope.Empty;

        public ActivityScope ContinueActivity(
            string activityName,
            string cacheKey,
            ActivityKind activityKind = ActivityKind.Internal,
            string? fallbackParentId = null,
            IEnumerable<KeyValuePair<string, object?>>? tags = null) => ActivityScope.Empty;

        public ActivityScope CreateLinkedActivity(
            string activityName,
            ActivityContext linkedContext,
            ActivityKind activityKind = ActivityKind.Internal,
            IEnumerable<KeyValuePair<string, object?>>? tags = null) => ActivityScope.Empty;
    }
}

/// <summary>
/// Test data generators.
/// </summary>
public static class TestDataGenerator
{
    public static Category CreateCategory(int index = 1)
    {
        return new Category
        {
            Name = $"Category-{index:D4}"
        };
    }

    public static Product CreateProduct(int index = 1, int categoryId = 1)
    {
        return new Product
        {
            Name = $"Product-{index:D6}",
            Description = $"Description for product {index}",
            Price = 10.00m + index * 0.99m,
            Quantity = index % 100 + 1,
            IsActive = index % 2 == 0,
            CreatedAt = DateTime.UtcNow.AddDays(-index),
            CategoryId = categoryId
        };
    }

    public static Order CreateOrder(int index = 1)
    {
        return new Order
        {
            OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{index:D6}",
            OrderDate = DateTime.UtcNow.AddDays(-index),
            TotalAmount = 100.00m * index,
            Status = index % 3 == 0 ? "Completed" : index % 2 == 0 ? "Processing" : "Pending"
        };
    }

    public static OrderItem CreateOrderItem(int orderId, int productId, int index = 1)
    {
        return new OrderItem
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = index % 10 + 1,
            UnitPrice = 25.00m + index * 1.50m
        };
    }

    public static ProductImportDto CreateProductImportDto(int index = 1, int categoryId = 1)
    {
        return new ProductImportDto
        {
            Name = $"ImportedProduct-{index:D6}",
            Description = $"Imported description {index}",
            Price = 15.00m + index * 1.25m,
            Quantity = index % 50 + 10,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CategoryId = categoryId
        };
    }

    public static Category[] CreateCategories(int count)
    {
        var categories = new Category[count];
        for (var i = 0; i < count; i++)
        {
            categories[i] = CreateCategory(i + 1);
        }
        return categories;
    }

    public static Product[] CreateProducts(int count, int categoryId = 1)
    {
        var products = new Product[count];
        for (var i = 0; i < count; i++)
        {
            products[i] = CreateProduct(i + 1, categoryId);
        }
        return products;
    }

    public static ProductImportDto[] CreateProductImportDtos(int count, int categoryId = 1)
    {
        var dtos = new ProductImportDto[count];
        for (var i = 0; i < count; i++)
        {
            dtos[i] = CreateProductImportDto(i + 1, categoryId);
        }
        return dtos;
    }
}
