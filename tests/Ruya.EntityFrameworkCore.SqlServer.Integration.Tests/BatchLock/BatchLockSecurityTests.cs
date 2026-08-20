using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer.BatchLock;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests.BatchLock;

[TestClass]
public sealed class BatchLockSecurityTests
{
    [TestMethod]
    public void SelectForUpdate_InvalidBatchSize_ThrowsValidationException()
    {
        using var context = CreateContext();
        var operations = new BatchLockOperations<BatchLockTestContext>(
            NullLogger<BatchLockOperations<BatchLockTestContext>>.Instance,
            context);
        var options = new BatchLockOptions
        {
            TableName = "BatchItems",
            LockedBy = "test",
            BatchSize = 0
        };

        Assert.ThrowsExactly<System.ComponentModel.DataAnnotations.ValidationException>(
            () => operations.SelectForUpdate<BatchItem>(options));
    }

    [TestMethod]
    public void SelectForUpdate_TableNameExceedsSqlIdentifierLimit_ThrowsValidationException()
    {
        using var context = CreateContext();
        var operations = new BatchLockOperations<BatchLockTestContext>(
            NullLogger<BatchLockOperations<BatchLockTestContext>>.Instance,
            context);
        var options = new BatchLockOptions
        {
            TableName = new string('t', 129),
            LockedBy = "test"
        };

        Assert.ThrowsExactly<System.ComponentModel.DataAnnotations.ValidationException>(
            () => operations.SelectForUpdate<BatchItem>(options));
    }

    [TestMethod]
    public void SelectForUpdate_LockedByExceedsSqlParameterLimit_ThrowsValidationException()
    {
        using var context = CreateContext();
        var operations = new BatchLockOperations<BatchLockTestContext>(
            NullLogger<BatchLockOperations<BatchLockTestContext>>.Instance,
            context);
        var options = new BatchLockOptions
        {
            TableName = "BatchItems",
            LockedBy = new string('l', 262)
        };

        Assert.ThrowsExactly<System.ComponentModel.DataAnnotations.ValidationException>(
            () => operations.SelectForUpdate<BatchItem>(options));
    }

    [TestMethod]
    public void SelectForUpdate_BlankOptionalIdentifier_ThrowsValidationException()
    {
        using var context = CreateContext();
        var operations = new BatchLockOperations<BatchLockTestContext>(
            NullLogger<BatchLockOperations<BatchLockTestContext>>.Instance,
            context);
        var options = new BatchLockOptions
        {
            TableName = "BatchItems",
            LockedBy = "test",
            PrimaryKeyField = " "
        };

        Assert.ThrowsExactly<System.ComponentModel.DataAnnotations.ValidationException>(
            () => operations.SelectForUpdate<BatchItem>(options));
    }

    [TestMethod]
    public void SelectForUpdate_ExcludedFieldExceedsSqlIdentifierLimit_ThrowsValidationException()
    {
        using var context = CreateContext();
        var operations = new BatchLockOperations<BatchLockTestContext>(
            NullLogger<BatchLockOperations<BatchLockTestContext>>.Instance,
            context);
        var options = new BatchLockOptions
        {
            TableName = "BatchItems",
            LockedBy = "test",
            ExcludeFields = new string('e', 129)
        };

        Assert.ThrowsExactly<System.ComponentModel.DataAnnotations.ValidationException>(
            () => operations.SelectForUpdate<BatchItem>(options));
    }

    private static BatchLockTestContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BatchLockTestContext>()
            .UseSqlServer("Server=127.0.0.1;Database=unused;User Id=unused;Password=unused;TrustServerCertificate=true")
            .Options;
        return new BatchLockTestContext(options);
    }

    private sealed class BatchLockTestContext(DbContextOptions<BatchLockTestContext> options) : DbContext(options)
    {
        public DbSet<BatchItem> BatchItems => Set<BatchItem>();
    }

    private sealed class BatchItem
    {
        public int Id { get; set; }
    }
}
