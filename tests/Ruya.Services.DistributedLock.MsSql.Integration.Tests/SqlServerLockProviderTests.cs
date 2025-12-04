using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.DistributedLock.MsSql.Providers;

namespace Ruya.Services.DistributedLock.MsSql.Tests;

/// <summary>
/// Unit tests for SqlServerLockProvider.
/// </summary>
/// <remarks>
/// These tests focus on validation and error handling.
/// Integration tests requiring actual SQL Server are not included.
/// </remarks>
[TestClass]
public class SqlServerLockProviderTests
{
    private Mock<ILogger<SqlServerLockProvider>> _mockLogger = null!;
    private const string TestConnectionString = "Server=localhost;Database=TestDb;Integrated Security=true;";

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<SqlServerLockProvider>>();
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_ShouldThrow_WhenConnectionStringIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new SqlServerLockProvider(null!, _mockLogger.Object);
        });
    }

    [TestMethod]
    public void Constructor_ShouldThrow_WhenConnectionStringIsEmpty()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            _ = new SqlServerLockProvider(string.Empty, _mockLogger.Object);
        });
    }

    [TestMethod]
    public void Constructor_ShouldThrow_WhenConnectionStringIsWhitespace()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
        {
            _ = new SqlServerLockProvider("   ", _mockLogger.Object);
        });
    }

    [TestMethod]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new SqlServerLockProvider(TestConnectionString, null!);
        });
    }

    [TestMethod]
    public void Constructor_ShouldSucceed_WithValidParameters()
    {
        // Act
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Assert
        Assert.IsNotNull(provider);
    }

    #endregion

    #region AcquireLockAsync Validation Tests

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await provider.AcquireLockAsync(null!, "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockKeyIsEmpty()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.AcquireLockAsync(string.Empty, "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockKeyIsWhitespace()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.AcquireLockAsync("   ", "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockValueIsNull()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await provider.AcquireLockAsync("key", null!, TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockValueIsEmpty()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.AcquireLockAsync("key", string.Empty, TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenLockValueIsWhitespace()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.AcquireLockAsync("key", "   ", TimeSpan.FromMinutes(1));
        });
    }

    #endregion

    #region ExtendLockAsync Validation Tests

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await provider.ExtendLockAsync(null!, "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenLockKeyIsEmpty()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.ExtendLockAsync(string.Empty, "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenLockValueIsNull()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await provider.ExtendLockAsync("key", null!, TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenLockValueIsEmpty()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.ExtendLockAsync("key", string.Empty, TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldReturnFalse_WhenLockDoesNotExist()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act
        var result = await provider.ExtendLockAsync("nonexistent-lock", "value", TimeSpan.FromMinutes(1));

        // Assert
        Assert.IsFalse(result, "ExtendLock should return false for nonexistent lock");
    }

    #endregion

    #region ReleaseLockAsync Validation Tests

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await provider.ReleaseLockAsync(null!, "value");
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenLockKeyIsEmpty()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.ReleaseLockAsync(string.Empty, "value");
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenLockValueIsNull()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await provider.ReleaseLockAsync("key", null!);
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenLockValueIsEmpty()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.ReleaseLockAsync("key", string.Empty);
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldReturnFalse_WhenLockDoesNotExist()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act
        var result = await provider.ReleaseLockAsync("nonexistent-lock", "value");

        // Assert
        Assert.IsFalse(result, "ReleaseLock should return false for nonexistent lock");
    }

    #endregion

    #region LockExistsAsync Validation Tests

    [TestMethod]
    public async Task LockExistsAsync_ShouldThrow_WhenLockKeyIsNull()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await provider.LockExistsAsync(null!);
        });
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldThrow_WhenLockKeyIsEmpty()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await provider.LockExistsAsync(string.Empty);
        });
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldReturnFalse_WhenLockDoesNotExist()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act
        var result = await provider.LockExistsAsync("nonexistent-lock");

        // Assert
        Assert.IsFalse(result, "LockExists should return false for nonexistent lock");
    }

    #endregion

    #region Dispose Tests

    [TestMethod]
    public async Task AcquireLockAsync_ShouldThrow_WhenDisposed()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);
        provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await provider.AcquireLockAsync("key", "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ExtendLockAsync_ShouldThrow_WhenDisposed()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);
        provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await provider.ExtendLockAsync("key", "value", TimeSpan.FromMinutes(1));
        });
    }

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldThrow_WhenDisposed()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);
        provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await provider.ReleaseLockAsync("key", "value");
        });
    }

    [TestMethod]
    public async Task LockExistsAsync_ShouldThrow_WhenDisposed()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);
        provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await provider.LockExistsAsync("key");
        });
    }

    [TestMethod]
    public void Dispose_ShouldBeIdempotent()
    {
        // Arrange
        var provider = new SqlServerLockProvider(TestConnectionString, _mockLogger.Object);

        // Act & Assert - Should not throw
        provider.Dispose();
        provider.Dispose();
        provider.Dispose();
    }

    #endregion
}
