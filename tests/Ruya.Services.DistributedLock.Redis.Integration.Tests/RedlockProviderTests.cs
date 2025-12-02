using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.DistributedLock.Redis.Configuration;
using Ruya.Services.DistributedLock.Redis.Providers;
using StackExchange.Redis;

namespace Ruya.Services.DistributedLock.Redis.Tests;

[TestClass]
public class RedlockProviderTests
{
    private Mock<ILogger<RedlockProvider>> _mockLogger = null!;
    private Mock<IOptions<RedisLockSettings>> _mockSettings = null!;
    private RedisLockSettings _settings = null!;
    private List<Mock<IConnectionMultiplexer>> _mockConnections = null!;
    private List<Mock<IDatabase>> _mockDatabases = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<RedlockProvider>>();
        _mockSettings = new Mock<IOptions<RedisLockSettings>>();
        _settings = new RedisLockSettings
        {
            RedlockEndpoints = new[] { "redis1", "redis2", "redis3" }, // 3 nodes, quorum = 2
            SyncTimeoutMs = 1000
        };
        _mockSettings.Setup(s => s.Value).Returns(_settings);

        _mockConnections = new List<Mock<IConnectionMultiplexer>>();
        _mockDatabases = new List<Mock<IDatabase>>();
    }

    private RedlockProvider CreateProvider()
    {
        int connectionIndex = 0;
        return new RedlockProvider(_mockSettings.Object, _mockLogger.Object, (config) =>
        {
            var mockConn = new Mock<IConnectionMultiplexer>();
            var mockDb = new Mock<IDatabase>();
            
            mockConn.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);
            mockConn.Setup(c => c.Configuration).Returns(config.ToString());
            
            _mockConnections.Add(mockConn);
            _mockDatabases.Add(mockDb);
            
            connectionIndex++;
            return mockConn.Object;
        });
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldSucceed_WhenAllNodesAcquire()
    {
        // Arrange
        var provider = CreateProvider();
        foreach (var db in _mockDatabases)
        {
            db.Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
        }

        // Act
        var result = await provider.AcquireLockAsync("key", "value", TimeSpan.FromSeconds(10));

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldSucceed_WhenQuorumAcquired()
    {
        // Arrange
        var provider = CreateProvider();
        
        // Node 1: Success
        _mockDatabases[0].Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        // Node 2: Success
        _mockDatabases[1].Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        // Node 3: Fail
        _mockDatabases[2].Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await provider.AcquireLockAsync("key", "value", TimeSpan.FromSeconds(10));

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldFail_WhenQuorumNotMet()
    {
        // Arrange
        var provider = CreateProvider();
        
        // Node 1: Success
        _mockDatabases[0].Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        // Node 2: Fail
        _mockDatabases[1].Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);
        // Node 3: Fail
        _mockDatabases[2].Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await provider.AcquireLockAsync("key", "value", TimeSpan.FromSeconds(10));

        // Assert
        Assert.IsFalse(result);
        
        // Verify release was called on ALL nodes (even failed ones, to be safe)
        foreach (var db in _mockDatabases)
        {
            db.Verify(d => d.LockReleaseAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()), Times.Once);
        }
    }

    [TestMethod]
    public async Task AcquireLockAsync_ShouldSucceed_WhenSingleInstanceUsed()
    {
        // Arrange
        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        mockMultiplexer.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);
        mockDb.Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var settings = new RedisLockSettings
        {
            RedlockEndpoints = null, // No endpoints
            SyncTimeoutMs = 1000
        };
        var mockSettings = new Mock<IOptions<RedisLockSettings>>();
        mockSettings.Setup(s => s.Value).Returns(settings);

        var provider = new RedlockProvider(mockSettings.Object, _mockLogger.Object, null, mockMultiplexer.Object);

        // Act
        var result = await provider.AcquireLockAsync("key", "value", TimeSpan.FromSeconds(10));

        // Assert
        Assert.IsTrue(result);
        mockDb.Verify(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()), Times.Once);
    }
}
