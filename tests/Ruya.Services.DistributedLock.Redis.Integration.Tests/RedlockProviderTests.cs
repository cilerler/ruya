using System;
using System.Collections.Generic;
using System.Linq;
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
    public async Task AcquireLockAsync_WhenCancelledDuringNodeCalls_CleansEveryNodeAndPreservesCancellation()
    {
        using var provider = CreateProvider();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        foreach (Mock<IDatabase> database in _mockDatabases)
        {
            database
                .Setup(db => db.LockTakeAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CommandFlags>()))
                .Returns(completion.Task);
            database
                .Setup(db => db.LockReleaseAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
        }

        _mockDatabases[0]
            .Setup(db => db.LockReleaseAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));

        Task<bool> acquire = provider.AcquireLockAsync(
            "cancelled-redlock",
            "owner",
            TimeSpan.FromSeconds(10),
            cancellation.Token);
        await cancellation.CancelAsync();
        completion.SetResult(true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => acquire);
        foreach (Mock<IDatabase> database in _mockDatabases)
        {
            database.Verify(
                db => db.LockReleaseAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<CommandFlags>()),
                Times.Once);
        }

        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Warning,
                It.Is<EventId>(eventId => eventId.Id == 8512 && eventId.Name == "RedlockNodeReleaseFailed"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExtendLockAsync_WhenCancellationArrivesWithFailedQuorum_ReturnsOwnershipFailure()
    {
        using var provider = CreateProvider();
        var allExtensionsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var extensionCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        int startedCount = 0;
        foreach (Mock<IDatabase> database in _mockDatabases)
        {
            database
                .Setup(db => db.LockExtendAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CommandFlags>()))
                .Returns(() =>
                {
                    if (Interlocked.Increment(ref startedCount) == _mockDatabases.Count)
                    {
                        allExtensionsStarted.TrySetResult();
                    }

                    return extensionCompleted.Task;
                });
        }

        Task<bool> operation = provider.ExtendLockAsync(
            "test-lock",
            "test-value",
            TimeSpan.FromSeconds(10),
            cancellation.Token);
        await allExtensionsStarted.Task;
        await cancellation.CancelAsync();
        extensionCompleted.TrySetResult(false);

        Assert.IsFalse(await operation);
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

    [TestMethod]
    public async Task ReleaseLockAsync_ShouldFail_WhenQuorumDoesNotConfirmRelease()
    {
        using var provider = CreateProvider();
        _mockDatabases[0]
            .Setup(d => d.LockReleaseAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _mockDatabases[1]
            .Setup(d => d.LockReleaseAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);
        _mockDatabases[2]
            .Setup(d => d.LockReleaseAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        bool released = await provider.ReleaseLockAsync("key", "value");

        Assert.IsFalse(released);
    }

    [TestMethod]
    public void Dispose_DoesNotDisposeInjectedMultiplexer()
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(new Mock<IDatabase>().Object);
        var settings = Options.Create(new RedisLockSettings { RedlockEndpoints = null });
        var provider = new RedlockProvider(settings, _mockLogger.Object, multiplexer: multiplexer.Object);

        provider.Dispose();

        multiplexer.Verify(c => c.Dispose(), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithTooFewDirectEndpoints_FailsBeforeCreatingConnections()
    {
        var settings = Options.Create(new RedisLockSettings
        {
            RedlockEndpoints = ["redis-1:6379", "redis-2:6379"]
        });
        int connectionAttempts = 0;

        Assert.Throws<ArgumentException>(() => new RedlockProvider(
            settings,
            _mockLogger.Object,
            _ =>
            {
                connectionAttempts++;
                return new Mock<IConnectionMultiplexer>().Object;
            }));

        Assert.AreEqual(0, connectionAttempts);
    }

    [TestMethod]
    public void Constructor_WithAliasedDirectEndpoints_FailsBeforeCreatingConnections()
    {
        var settings = Options.Create(new RedisLockSettings
        {
            RedlockEndpoints =
            [
                "REDIS-1,ssl=false,abortConnect=false",
                "redis-1:6379,abortConnect=false,ssl=false",
                "redis-3:6379"
            ]
        });
        int connectionAttempts = 0;

        Assert.Throws<ArgumentException>(() => new RedlockProvider(
            settings,
            _mockLogger.Object,
            _ =>
            {
                connectionAttempts++;
                return new Mock<IConnectionMultiplexer>().Object;
            }));

        Assert.AreEqual(0, connectionAttempts);
    }

    [TestMethod]
    public void Dispose_WhenConnectionsAreOwned_DisposesEachConnectionExactlyOnce()
    {
        var provider = CreateProvider();

        provider.Dispose();
        provider.Dispose();

        foreach (Mock<IConnectionMultiplexer> connection in _mockConnections)
        {
            connection.Verify(item => item.Dispose(), Times.Once);
        }
    }

    [TestMethod]
    public async Task AcquireLockAsync_WhenNodeFails_DoesNotPutEndpointCredentialInLogState()
    {
        const string secret = "super-secret";
        _settings.RedlockEndpoints =
        [
            $"redis1,password={secret}",
            "redis2",
            "redis3"
        ];
        using var provider = CreateProvider();
        _mockDatabases[0]
            .Setup(d => d.LockTakeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("node failed"));

        await provider.AcquireLockAsync("key", "value", TimeSpan.FromSeconds(10));

        Assert.IsFalse(_mockLogger.Invocations.Any(invocation =>
            invocation.Arguments.Count > 2 &&
            invocation.Arguments[2]?.ToString()?.Contains(secret, StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public async Task AcquireLockAsync_WithNonPositiveExpiry_ThrowsBeforeCallingRedis()
    {
        using var provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.AcquireLockAsync("key", "value", TimeSpan.Zero));

        foreach (var database in _mockDatabases)
        {
            database.VerifyNoOtherCalls();
        }
    }

    [TestMethod]
    public void Constructor_WhenAConnectionFails_DisposesConnectionsItAlreadyCreated()
    {
        var firstConnection = new Mock<IConnectionMultiplexer>();
        int callCount = 0;

        Assert.Throws<InvalidOperationException>(() => new RedlockProvider(
            _mockSettings.Object,
            _mockLogger.Object,
            _ => ++callCount == 1
                ? firstConnection.Object
                : throw new InvalidOperationException("connection failed")));

        firstConnection.Verify(connection => connection.Dispose(), Times.Once);
    }
}
