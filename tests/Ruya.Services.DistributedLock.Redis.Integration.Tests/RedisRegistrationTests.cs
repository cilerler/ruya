using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Redis.Configuration;
using Ruya.Services.DistributedLock.Redis.Extensions;
using Ruya.Services.DistributedLock.Redis.Providers;
using StackExchange.Redis;

namespace Ruya.Services.DistributedLock.Redis.Tests;

[TestClass]
public sealed class RedisRegistrationTests
{
    [TestMethod]
    public void AddRedisDistributedLock_ValidatesCatalogWithoutCopyingSecretIntoOptions()
    {
        using ServiceProvider provider = BuildProvider(
            services => services.AddRedisDistributedLock(),
            new Dictionary<string, string?>
            {
                ["DistributedLock:Redis:ConnectionStringKey"] = "RedisLocks",
                ["ConnectionStrings:RedisLocks"] = "localhost:6379,ssl=false"
            });

        RedisLockSettings settings = provider.GetRequiredService<IOptions<RedisLockSettings>>().Value;

        Assert.AreEqual("RedisLocks", settings.ConnectionStringKey);
        Assert.IsNull(settings.ConnectionString);
    }

    [TestMethod]
    public void AddRedisDistributedLock_WhenCatalogEntryIsMissing_FailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(
            services => services.AddRedisDistributedLock(),
            new Dictionary<string, string?>
            {
                ["DistributedLock:Redis:ConnectionStringKey"] = "MissingRedis"
            });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<RedisLockSettings>>().Value);
    }

    [TestMethod]
    public void AddRedisDistributedLock_CallerSuppliedMultiplexerWithoutCatalog_ReusesBorrowedConnection()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var database = new Mock<IDatabase>();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton(multiplexer.Object);
        services.AddRedisDistributedLock();

        Assert.AreEqual(
            1,
            services.Count(descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer)));

        ServiceProvider provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IOptions<RedisLockSettings>>().Value;
        IConnectionMultiplexer resolvedConnection = provider.GetRequiredService<IConnectionMultiplexer>();
        IDistributedLockProvider lockProvider = provider.GetRequiredService<IDistributedLockProvider>();

        Assert.AreSame(multiplexer.Object, resolvedConnection);
        Assert.IsInstanceOfType<RedisLockProvider>(lockProvider);
        multiplexer.Verify(
            connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object>()),
            Times.Once);

        provider.Dispose();
        multiplexer.Verify(connection => connection.Dispose(), Times.Never);
    }

    [TestMethod]
    public void AddRedisDistributedLock_OnlyKeyedMultiplexerWithoutCatalog_RejectsMissingConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddKeyedSingleton<IConnectionMultiplexer>("secondary", multiplexer.Object);

        services.AddRedisDistributedLock();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<RedisLockSettings>>().Value);
    }

    [TestMethod]
    public void AddRedlockDistributedLock_WithCatalogKeys_DoesNotRequireSingleRedisKey()
    {
        using ServiceProvider provider = BuildProvider(
            services => services.AddRedlockDistributedLock(),
            new Dictionary<string, string?>
            {
                ["DistributedLock:Redis:RedlockConnectionStringKeys:0"] = "RedisNode1",
                ["DistributedLock:Redis:RedlockConnectionStringKeys:1"] = "RedisNode2",
                ["DistributedLock:Redis:RedlockConnectionStringKeys:2"] = "RedisNode3",
                ["ConnectionStrings:RedisNode1"] = "redis-1:6379",
                ["ConnectionStrings:RedisNode2"] = "redis-2:6379",
                ["ConnectionStrings:RedisNode3"] = "redis-3:6379"
            });

        RedisLockSettings settings = provider.GetRequiredService<IOptions<RedisLockSettings>>().Value;

        CollectionAssert.AreEqual(
            new[] { "RedisNode1", "RedisNode2", "RedisNode3" },
            settings.RedlockConnectionStringKeys);
        Assert.IsNull(settings.RedlockEndpoints);
    }

    [TestMethod]
    public void AddRedlockDistributedLock_WithDuplicateCatalogKeys_FailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(
            services => services.AddRedlockDistributedLock(),
            new Dictionary<string, string?>
            {
                ["DistributedLock:Redis:RedlockConnectionStringKeys:0"] = "RedisNode1",
                ["DistributedLock:Redis:RedlockConnectionStringKeys:1"] = "RedisNode1",
                ["DistributedLock:Redis:RedlockConnectionStringKeys:2"] = "RedisNode3",
                ["ConnectionStrings:RedisNode1"] = "redis-1:6379",
                ["ConnectionStrings:RedisNode3"] = "redis-3:6379"
            });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<RedisLockSettings>>().Value);
    }

    [TestMethod]
    public void AddRedlockDistributedLock_WithDistinctKeysResolvingToSameNode_FailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(
            services => services.AddRedlockDistributedLock(),
            new Dictionary<string, string?>
            {
                ["DistributedLock:Redis:RedlockConnectionStringKeys:0"] = "RedisNode1",
                ["DistributedLock:Redis:RedlockConnectionStringKeys:1"] = "RedisNodeAlias",
                ["DistributedLock:Redis:RedlockConnectionStringKeys:2"] = "RedisNode3",
                ["ConnectionStrings:RedisNode1"] = "REDIS-1,ssl=false,abortConnect=false",
                ["ConnectionStrings:RedisNodeAlias"] = "redis-1:6379,abortConnect=false,ssl=false",
                ["ConnectionStrings:RedisNode3"] = "redis-3:6379"
            });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<RedisLockSettings>>().Value);
    }

    [TestMethod]
    public void AddRedlockDistributedLock_WithOneVoteContainingMultipleNodes_FailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(
            services => services.AddRedlockDistributedLock(),
            new Dictionary<string, string?>
            {
                ["DistributedLock:Redis:RedlockEndpoints:0"] = "redis-1:6379,redis-1-backup:6379",
                ["DistributedLock:Redis:RedlockEndpoints:1"] = "redis-2:6379",
                ["DistributedLock:Redis:RedlockEndpoints:2"] = "redis-3:6379"
            });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<RedisLockSettings>>().Value);
    }

    [TestMethod]
    public void AddRedlockDistributedLock_WithCallerSuppliedMultiplexer_PreservesProgrammaticRegistration()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton(multiplexer.Object);
        services.AddRedlockDistributedLock();

        using ServiceProvider provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IOptions<RedisLockSettings>>().Value;
        IDistributedLockProvider lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        Assert.IsInstanceOfType<RedlockProvider>(lockProvider);

        ((IDisposable)lockProvider).Dispose();
        multiplexer.Verify(connection => connection.Dispose(), Times.Never);
    }

    private static ServiceProvider BuildProvider(
        System.Action<IServiceCollection> register,
        Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        register(services);
        return services.BuildServiceProvider();
    }
}
