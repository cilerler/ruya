using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer.BatchLock;
using Ruya.EntityFrameworkCore.SqlServer.Tests.TestInfrastructure;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests.BatchLock;

[TestClass]
public sealed class BatchLockIntegrationTests
{
    private static readonly int[] CustomDescendingExpectedOrders = [2, 3];
    private static readonly int[] DefaultExpectedOrders = [3, 4];
    private static SqlServerFixture? _fixture;

    private static SqlServerFixture Fixture =>
        _fixture ?? throw new InvalidOperationException("The SQL Server fixture has not been initialized.");

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _fixture = new SqlServerFixture();
        await _fixture.InitializeAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await Fixture.CleanTablesAsync();
        await using var context = Fixture.CreateDbContext();
        context.BatchItems.AddRange(
            CreateBatchItem(groupId: 10, processingOrder: 3),
            CreateBatchItem(groupId: 10, processingOrder: 1),
            CreateBatchItem(groupId: 20, processingOrder: 2));
        await context.SaveChangesAsync();
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_ReadCommittedSnapshotEnabled_LocksOrderedBatch()
    {
        await using var context = Fixture.CreateDbContext();
        var operations = CreateOperations(context);
        Assert.AreEqual(1, await ReadSessionValueAsync(
            context,
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()"));

        var keys = await operations.SelectForUpdateKeysAsync<int>(CreateOptions(batchSize: 2));

        Assert.AreEqual(2, keys.Count);
        var updatedOrders = await context.BatchItems
            .Where(item => item.ProcessStatusCode == 1)
            .OrderBy(item => item.ProcessingOrder)
            .Select(item => item.ProcessingOrder)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 1, 2 }, updatedOrders);
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_CustomWhereClause_ReplacesStructuredPredicateAndSelectsMatchingRows()
    {
        await using var context = Fixture.CreateDbContext();
        var customMatch = await context.BatchItems.SingleAsync(item => item.GroupId == 20);
        customMatch.ProcessStatusCode = 7;
        customMatch.SoftDelete = true;
        await context.SaveChangesAsync();
        var customMatchId = customMatch.Id;
        context.ChangeTracker.Clear();
        var options = CreateOptions(
            batchSize: 3,
            whereClause: "t.[GroupId] = 20");

        var keys = await CreateOperations(context).SelectForUpdateKeysAsync<int>(options);

        CollectionAssert.AreEqual(new[] { customMatchId }, keys);
        var persistedMatch = await context.BatchItems
            .AsNoTracking()
            .SingleAsync(item => item.Id == customMatchId);
        Assert.AreEqual(1, persistedMatch.ProcessStatusCode);
        Assert.AreEqual(1, persistedMatch.LockState);
        Assert.AreEqual(0, await context.BatchItems.CountAsync(item => item.GroupId != 20 && item.LockState != 0));
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_CustomOrderByClause_ControlsTopRows()
    {
        await using var context = Fixture.CreateDbContext();
        var expectedKeys = await context.BatchItems
            .OrderByDescending(item => item.ProcessingOrder)
            .Take(2)
            .Select(item => item.Id)
            .ToListAsync();
        var options = CreateOptions(
            batchSize: 2,
            orderByClause: "t.[ProcessingOrder] DESC");

        var keys = await CreateOperations(context).SelectForUpdateKeysAsync<int>(options);

        CollectionAssert.AreEqual(
            expectedKeys.OrderBy(key => key).ToList(),
            keys.OrderBy(key => key).ToList());
        var lockedOrders = await context.BatchItems
            .Where(item => item.LockState == 1)
            .OrderBy(item => item.ProcessingOrder)
            .Select(item => item.ProcessingOrder)
            .ToListAsync();
        CollectionAssert.AreEqual(CustomDescendingExpectedOrders, lockedOrders);
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_NullCustomClauses_UsesStructuredFilteringAndOrdering()
    {
        await using var context = Fixture.CreateDbContext();
        var statusExcluded = await context.BatchItems.SingleAsync(item => item.ProcessingOrder == 1);
        statusExcluded.ProcessStatusCode = 9;
        var softDeleted = await context.BatchItems.SingleAsync(item => item.ProcessingOrder == 2);
        softDeleted.SoftDelete = true;
        var excludedIds = new[] { statusExcluded.Id, softDeleted.Id };
        context.BatchItems.AddRange(
            CreateBatchItem(groupId: 30, processingOrder: 4),
            CreateBatchItem(groupId: 30, processingOrder: 5));
        await context.SaveChangesAsync();
        var options = CreateOptions(batchSize: 2);

        var keys = await CreateOperations(context).SelectForUpdateKeysAsync<int>(options);

        Assert.AreEqual(2, keys.Count);
        var lockedOrders = await context.BatchItems
            .Where(item => item.LockState == 1)
            .OrderBy(item => item.ProcessingOrder)
            .Select(item => item.ProcessingOrder)
            .ToListAsync();
        CollectionAssert.AreEqual(DefaultExpectedOrders, lockedOrders);
        Assert.AreEqual(0, await context.BatchItems
            .AsNoTracking()
            .CountAsync(item => excludedIds.Contains(item.Id) && item.LockState != 0));
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_SerializableCallerTransaction_PreservesIsolationLevel()
    {
        await using var context = Fixture.CreateDbContext();
        var operations = CreateOperations(context);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var before = await ReadSessionValueAsync(
            context,
            "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID");

        var keys = await operations.SelectForUpdateKeysAsync<int>(CreateOptions(batchSize: 1));
        var after = await ReadSessionValueAsync(
            context,
            "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID");

        Assert.AreEqual(1, keys.Count);
        Assert.AreEqual(4, before);
        Assert.AreEqual(before, after);
        await transaction.RollbackAsync();
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_ConcurrentReadCommittedTransactions_SkipLockedRowsUnderRcsi()
    {
        await using var firstContext = Fixture.CreateDbContext();
        await using var secondContext = Fixture.CreateDbContext();
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var firstKeys = await CreateOperations(firstContext)
            .SelectForUpdateKeysAsync<int>(CreateOptions(batchSize: 1));

        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        var secondKeys = await CreateOperations(secondContext)
            .SelectForUpdateKeysAsync<int>(CreateOptions(batchSize: 1), timeout.Token);

        Assert.AreEqual(1, firstKeys.Count);
        Assert.AreEqual(1, secondKeys.Count);
        Assert.AreNotEqual(firstKeys.Single(), secondKeys.Single());
        await secondTransaction.RollbackAsync();
        await firstTransaction.RollbackAsync();
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_MissingStatusFilterField_FailsClosed()
    {
        await using var context = Fixture.CreateDbContext();
        var options = CreateOptions(batchSize: 1, processStatusCodeField: "MissingStatus");

        await AssertSqlErrorAsync(
            50005,
            () => CreateOperations(context).SelectForUpdateKeysAsync<int>(options));
        await AssertAllItemsRemainUnlockedAsync(context);
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_MissingStatusUpdateField_FailsClosed()
    {
        await using var context = Fixture.CreateDbContext();
        var options = new BatchLockOptions
        {
            TableName = "BatchItems",
            LockedBy = "integration-test",
            BatchSize = 1,
            ProcessStatusCodeField = "MissingStatus",
            UpdateProcessStatusCode = true,
            ProcessStatusCodeNextValue = 1
        };

        await AssertSqlErrorAsync(
            50006,
            () => CreateOperations(context).SelectForUpdateKeysAsync<int>(options));
        await AssertAllItemsRemainUnlockedAsync(context);
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_MissingExplicitOrderField_FailsClosed()
    {
        await using var context = Fixture.CreateDbContext();
        var options = CreateOptions(batchSize: 1, processingOrderField: "MissingOrder");

        await AssertSqlErrorAsync(
            50007,
            () => CreateOperations(context).SelectForUpdateKeysAsync<int>(options));
        await AssertAllItemsRemainUnlockedAsync(context);
    }

    [TestMethod]
    public async Task SelectForUpdateKeysAsync_NonUniqueJoinField_FailsClosed()
    {
        await using var context = Fixture.CreateDbContext();
        var options = CreateOptions(batchSize: 1, primaryKeyField: nameof(BatchItem.GroupId));

        await AssertSqlErrorAsync(
            50003,
            () => CreateOperations(context).SelectForUpdateKeysAsync<int>(options));
        await AssertAllItemsRemainUnlockedAsync(context);
    }

    private static BatchLockOperations<TestDbContext> CreateOperations(TestDbContext context) =>
        new(NullLogger<BatchLockOperations<TestDbContext>>.Instance, context);

    private static BatchItem CreateBatchItem(int groupId, int processingOrder) => new()
    {
        GroupId = groupId,
        ProcessingOrder = processingOrder,
        ProcessStatusCode = 0,
        LockState = 0,
        ModifiedAt = DateTime.UtcNow
    };

    private static BatchLockOptions CreateOptions(
        int batchSize,
        string processStatusCodeField = nameof(BatchItem.ProcessStatusCode),
        string processingOrderField = nameof(BatchItem.ProcessingOrder),
        string primaryKeyField = nameof(BatchItem.Id),
        string? whereClause = null,
        string? orderByClause = null) => new()
    {
        TableName = "BatchItems",
        LockedBy = "integration-test",
        BatchSize = batchSize,
        ProcessStatusCodeField = processStatusCodeField,
        ProcessStatusCodeValue = 0,
        UpdateProcessStatusCode = true,
        ProcessStatusCodeNextValue = 1,
        ProcessingOrderField = processingOrderField,
        PrimaryKeyField = primaryKeyField,
        WhereClause = whereClause,
        OrderByClause = orderByClause
    };

    private static async Task<int> ReadSessionValueAsync(TestDbContext context, string commandText)
    {
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            command.Transaction = (SqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task AssertSqlErrorAsync(int expectedNumber, Func<Task> action)
    {
        var exception = await Assert.ThrowsExactlyAsync<SqlException>(action);
        Assert.AreEqual(expectedNumber, exception.Number);
    }

    private static async Task AssertAllItemsRemainUnlockedAsync(TestDbContext context)
    {
        Assert.AreEqual(3, await context.BatchItems.CountAsync(item => item.ProcessStatusCode == 0));
        Assert.AreEqual(0, await context.BatchItems.CountAsync(item => item.LockState != 0));
    }
}
