using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.Redis;
using Ruya.Services.MessageQueue.Serialization;

namespace Ruya.Services.MessageQueue.Redis.Integration.Tests;

[TestClass]
public sealed class RedisProviderContractTests
{
    [TestMethod]
    public void Validate_PublishModesConfigured_RequiresExactlyOneMode()
    {
        var validator = new RedisOptionsValidator();

        var neither = validator.Validate(null, CreateOptions(usePubSub: false, useStreams: false));
        var both = validator.Validate(null, CreateOptions(usePubSub: true, useStreams: true));
        var pubSub = validator.Validate(null, CreateOptions(usePubSub: true, useStreams: false));
        var streams = validator.Validate(null, CreateOptions(usePubSub: false, useStreams: true));

        Assert.IsTrue(neither.Failed);
        Assert.IsTrue(both.Failed);
        Assert.IsTrue(pubSub.Succeeded);
        Assert.IsTrue(streams.Succeeded);
    }

    [TestMethod]
    public void Validate_TimeoutExceedsRedisIntegerLimit_Fails()
    {
        var validator = new RedisOptionsValidator();
        var options = CreateOptions(usePubSub: true, useStreams: false);
        options.ConnectionTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1);

        var result = validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Capabilities_PubSubProvider_DoesNotPromiseUnsupportedGuarantees()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(_ => { })
            .AddRedis(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.UsePubSub = true;
                options.UseStreams = false;
            });

        using var provider = services.BuildServiceProvider();
        var redis = provider.GetServices<IMessageQueueProvider>()
            .Single(candidate => candidate.ProviderName == "Redis");

        Assert.IsFalse(redis.Capabilities.SupportsConsumerGroups);
        Assert.IsFalse(redis.Capabilities.SupportsReplay);
        Assert.IsFalse(redis.Capabilities.SupportsTimeToLive);
        Assert.IsFalse(redis.Capabilities.SupportsDeadLetterQueue);
    }

    [TestMethod]
    public void PublicApi_ReleasedConfigurationAndConstructorSignatures_RemainAsObsoleteBridges()
    {
        var configurationOverload = typeof(RedisExtensions)
            .GetMethods()
            .Single(method =>
            {
                if (method.Name != nameof(RedisExtensions.AddRedis))
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2 && parameters[1].ParameterType == typeof(IConfiguration);
            });
        var providerConstructor = typeof(RedisProvider).GetConstructor(
        [
            typeof(IOptions<RedisOptions>),
            typeof(IMessageSerializer),
            typeof(IEnumerable<IMessageMiddleware>),
            typeof(ILogger<RedisProvider>),
        ]);

        Assert.IsNotNull(configurationOverload.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
        Assert.IsNotNull(providerConstructor);
        Assert.IsNotNull(providerConstructor.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
    }

    [TestMethod]
    public void AddRedis_ConfigurationSectionPresent_BindsTypedOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RedisMessageQueue"] = "localhost:6380",
                [$"{RedisOptions.ConfigurationSectionName}:RedisConnectionStringKey"] = "RedisMessageQueue",
                [$"{RedisOptions.ConfigurationSectionName}:UsePubSub"] = "true",
                [$"{RedisOptions.ConfigurationSectionName}:UseStreams"] = "false",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMessageQueue(_ => { }).AddRedis();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;

        Assert.AreEqual("localhost:6380", options.ConnectionString);
        Assert.AreEqual("RedisMessageQueue", options.RedisConnectionStringKey);
        Assert.IsTrue(options.UsePubSub);
        Assert.IsFalse(options.UseStreams);
    }

    [TestMethod]
    public void AddRedis_TypedConfigurationWithoutConfigurationService_PreservesReleasedBehavior()
    {
        var services = new ServiceCollection();
        services.AddMessageQueue(_ => { }).AddRedis(options =>
        {
            options.ConnectionString = "localhost:6381";
            options.UsePubSub = true;
            options.UseStreams = false;
        });
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;

        Assert.AreEqual("localhost:6381", options.ConnectionString);
    }

    [TestMethod]
    public void AddRedis_ReleasedConfigurationOverload_StillResolvesCatalogKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CompatibilityRedis"] = "localhost:6382",
                [$"{RedisOptions.ConfigurationSectionName}:RedisConnectionStringKey"] = "CompatibilityRedis",
                [$"{RedisOptions.ConfigurationSectionName}:UsePubSub"] = "true",
                [$"{RedisOptions.ConfigurationSectionName}:UseStreams"] = "false",
            })
            .Build();
        var services = new ServiceCollection();
#pragma warning disable CS0618 // Regression coverage for the released 8.x overload.
        services.AddMessageQueue(_ => { }).AddRedis(configuration);
#pragma warning restore CS0618
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;

        Assert.AreEqual("localhost:6382", options.ConnectionString);
    }

    [TestMethod]
    public void AddRedis_ConnectionStringKeyMissingFromCatalog_FailsWithoutEchoingSecrets()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RedisOptions.ConfigurationSectionName}:RedisConnectionStringKey"] = "MissingRedis",
                [$"{RedisOptions.ConfigurationSectionName}:UsePubSub"] = "true",
                [$"{RedisOptions.ConfigurationSectionName}:UseStreams"] = "false",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMessageQueue(_ => { }).AddRedis();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<RedisOptions>>().Value);

        StringAssert.Contains(exception.Message, "RedisConnectionStringKey");
        Assert.IsFalse(exception.Message.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task CreateAsync_CallerTokenAlreadyCanceled_ThrowsOperationCanceledException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(_ => { })
            .AddRedis(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.UsePubSub = true;
                options.UseStreams = false;
            });
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetServices<IMessageQueueProvider>()
            .Single(candidate => candidate.ProviderName == "Redis");
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => provider.CreateAsync("canceled", cancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task DisposeAsync_ConcurrentCalls_CompleteWithoutRedisConnection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(_ => { })
            .AddRedis(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.UsePubSub = true;
                options.UseStreams = false;
            });
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetServices<IMessageQueueProvider>()
            .Single(candidate => candidate.ProviderName == "Redis");
        var queue = await provider.CreateAsync("dispose");

        await Task.WhenAll(DisposeAsync(queue), DisposeAsync(queue));
    }

    private static async Task DisposeAsync(IAsyncDisposable disposable) => await disposable.DisposeAsync();

    private static RedisOptions CreateOptions(bool usePubSub, bool useStreams)
    {
        return new RedisOptions
        {
            ConnectionString = "localhost:6379",
            UsePubSub = usePubSub,
            UseStreams = useStreams
        };
    }
}
