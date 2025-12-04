using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.DistributedLock.Common;

namespace Ruya.Services.DistributedLock.MsSql.Tests;

/// <summary>
/// Tests for lock validation in the context of SQL Server provider.
/// </summary>
[TestClass]
public class LockValidationTests
{
    #region ValidateLockKey Tests

    [TestMethod]
    public void ValidateLockKey_ShouldThrow_WhenNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            LockValidation.ValidateLockKey(null!);
        });
    }

    [TestMethod]
    public void ValidateLockKey_ShouldThrow_WhenEmpty()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LockValidation.ValidateLockKey(string.Empty);
        });
    }

    [TestMethod]
    public void ValidateLockKey_ShouldThrow_WhenWhitespace()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LockValidation.ValidateLockKey("   ");
        });
    }

    [TestMethod]
    public void ValidateLockKey_ShouldSucceed_WithValidKey()
    {
        // Arrange
        var validKeys = new[]
        {
            "simple-key",
            "key.with.dots",
            "key_with_underscores",
            "key:with:colons",
            "key/with/slashes",
            "UPPERCASE",
            "lowercase",
            "MixedCase123",
            "key-with-numbers-12345"
        };

        // Act & Assert - Should not throw
        foreach (var key in validKeys)
        {
            LockValidation.ValidateLockKey(key);
        }
    }

    #endregion

    #region ValidateLockValue Tests

    [TestMethod]
    public void ValidateLockValue_ShouldThrow_WhenNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            LockValidation.ValidateLockValue(null!);
        });
    }

    [TestMethod]
    public void ValidateLockValue_ShouldThrow_WhenEmpty()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LockValidation.ValidateLockValue(string.Empty);
        });
    }

    [TestMethod]
    public void ValidateLockValue_ShouldThrow_WhenWhitespace()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            LockValidation.ValidateLockValue("   ");
        });
    }

    [TestMethod]
    public void ValidateLockValue_ShouldSucceed_WithValidValue()
    {
        // Arrange
        var validValues = new[]
        {
            "simple-value",
            "value.with.dots",
            "value_with_underscores",
            Guid.NewGuid().ToString(),
            "timestamp-2024-01-01",
            "machine-name-12345",
            "process-id-67890"
        };

        // Act & Assert - Should not throw
        foreach (var value in validValues)
        {
            LockValidation.ValidateLockValue(value);
        }
    }

    #endregion

    #region SQL Server Specific Validation Tests

    [TestMethod]
    public void LockKey_ShouldWork_WithSqlServerResourceNameLimits()
    {
        // SQL Server sp_getapplock resource names can be up to 255 characters
        // Arrange
        var longKey = new string('a', 255);

        // Act & Assert - Should not throw
        LockValidation.ValidateLockKey(longKey);
    }

    [TestMethod]
    public void LockKey_ShouldWork_WithCommonDatabaseNamingConventions()
    {
        // Arrange - Common database/resource naming patterns
        var databaseStyleKeys = new[]
        {
            "schema.table.lock",
            "[dbo].[users].lock",
            "Database_Table_Lock",
            "APP_LOCK_RESOURCE_1"
        };

        // Act & Assert - Should not throw
        foreach (var key in databaseStyleKeys)
        {
            LockValidation.ValidateLockKey(key);
        }
    }

    [TestMethod]
    public void LockValue_ShouldWork_WithMachineAndProcessIdentifiers()
    {
        // Arrange - Common lock value patterns for SQL Server scenarios
        var identifiers = new[]
        {
            $"{Environment.MachineName}-{Environment.ProcessId}",
            $"{Environment.UserName}@{Environment.MachineName}",
            $"session-{Guid.NewGuid()}",
            $"worker-{Environment.ProcessId}-{DateTimeOffset.UtcNow.Ticks}"
        };

        // Act & Assert - Should not throw
        foreach (var identifier in identifiers)
        {
            LockValidation.ValidateLockValue(identifier);
        }
    }

    #endregion
}
