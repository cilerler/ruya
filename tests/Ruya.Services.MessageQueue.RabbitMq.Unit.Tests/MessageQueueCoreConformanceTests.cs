using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.Health;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Serialization;
using CoreRegistrationExtensions = Ruya.Services.MessageQueue.Extensions.ServiceCollectionExtensions;

namespace Ruya.Services.MessageQueue.RabbitMq.Unit.Tests;

[TestClass]
public sealed class MessageQueueCoreConformanceTests
{
    private static readonly string[] _ordersPrimaryQueueName = ["orders-primary"];

    [TestMethod]
    public async Task AddMessageQueue_ConfigurationSectionPresent_BindsAndValidatesOnStartup()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{MessageQueueOptions.ConfigurationSectionName}:DefaultTimeout"] = "00:00:07",
            [$"{MessageQueueOptions.ConfigurationSectionName}:DefaultProvider"] = "orders-primary",
            [$"{MessageQueueOptions.ConfigurationSectionName}:Providers:orders-primary:Enabled"] = "true",
            [$"{MessageQueueOptions.ConfigurationSectionName}:Providers:orders-primary:Type"] = "RabbitMQ"
        });
        builder.Services.AddMessageQueue();
        using var host = builder.Build();

        // Act
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<MessageQueueOptions>>().Value;

        // Assert
        Assert.AreEqual(TimeSpan.FromSeconds(7), options.DefaultTimeout);
        Assert.AreEqual("orders-primary", options.DefaultProvider);
        Assert.AreEqual("RabbitMQ", options.Providers["orders-primary"].Type);

        await host.StopAsync();
    }

    [TestMethod]
    public async Task AddMessageQueue_DefaultProviderMissing_ThrowsOnStartup()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{MessageQueueOptions.ConfigurationSectionName}:DefaultProvider"] = "missing",
            [$"{MessageQueueOptions.ConfigurationSectionName}:Providers:orders-primary:Enabled"] = "true",
            [$"{MessageQueueOptions.ConfigurationSectionName}:Providers:orders-primary:Type"] = "RabbitMQ"
        });
        builder.Services.AddMessageQueue();
        using var host = builder.Build();

        // Act
        var exception = await Assert.ThrowsExactlyAsync<OptionsValidationException>(
            () => host.StartAsync());

        // Assert
        StringAssert.Contains(exception.Message, "DefaultProvider", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task AddMessageQueue_SharedProviderConnectionStringConfigured_FailsWithoutEchoingCredential()
    {
        const string credential = "do-not-echo-this-credential";
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{MessageQueueOptions.ConfigurationSectionName}:Providers:orders:Enabled"] = "false",
            [$"{MessageQueueOptions.ConfigurationSectionName}:Providers:orders:Type"] = "Redis",
            [$"{MessageQueueOptions.ConfigurationSectionName}:Providers:orders:ConnectionString"] =
                $"localhost:6379,password={credential}",
        });
        builder.Services.AddMessageQueue();
        using var host = builder.Build();

        var exception = await Assert.ThrowsExactlyAsync<OptionsValidationException>(
            () => host.StartAsync());

        StringAssert.Contains(exception.Message, "ProviderConfiguration.ConnectionString", StringComparison.Ordinal);
        StringAssert.Contains(exception.Message, "*ConnectionStringKey", StringComparison.Ordinal);
        Assert.IsFalse(exception.Message.Contains(credential, StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProviderConfiguration_ReleasedConnectionString_RemainsObsoleteCompatibilityBridge()
    {
        var property = typeof(ProviderConfiguration).GetProperty(
            "ConnectionString");

        Assert.IsNotNull(property);
        Assert.IsNotNull(property.GetCustomAttribute<ObsoleteAttribute>());
    }

    [TestMethod]
    public async Task AddMessageQueue_UnsupportedNamedSerializer_ThrowsOnStartup()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddMessageQueue(options => options.Serializer = "messagepack");
        using var host = builder.Build();

        // Act
        var exception = await Assert.ThrowsExactlyAsync<OptionsValidationException>(
            () => host.StartAsync());

        // Assert
        StringAssert.Contains(exception.Message, "Serializer", StringComparison.Ordinal);
        StringAssert.Contains(exception.Message, "json", StringComparison.Ordinal);
    }

    [TestMethod]
    public void RegistrationExtensions_Legacy8xSurface_RemainsAsObsoleteCompatibilityBridges()
    {
        // Arrange
        var coreMethods = typeof(CoreRegistrationExtensions).GetMethods(
            BindingFlags.Public | BindingFlags.Static);
        var rabbitMethods = typeof(RabbitMQExtensions).GetMethods(
            BindingFlags.Public | BindingFlags.Static);

        // Act
        var coreConfigurationOverload = coreMethods.Single(static method =>
            method.Name == "AddMessageQueue" &&
            method.GetParameters().Length == 2 &&
            method.GetParameters()[1].ParameterType == typeof(IConfiguration));
        var rabbitConfigurationOverload = rabbitMethods.Single(static method =>
            method.Name == "AddRabbitMQ" &&
            method.GetParameters().Length == 2 &&
            method.GetParameters()[1].ParameterType == typeof(IConfiguration));
        var singletonOverload = coreMethods.Single(static method =>
            method.Name == "AddSingletonMessageQueue");
        var aggregateHealthConstructor = typeof(MessageQueueHealthCheck).GetConstructor(
            [typeof(IMessageQueueFactory)]);
        var telemetryMiddlewareConstructor = typeof(TelemetryMiddleware).GetConstructor(
            [typeof(ILogger<TelemetryMiddleware>)]);
        var rabbitProviderConstructor = typeof(RabbitMQProvider).GetConstructor(
            [
                typeof(IOptions<RabbitMQOptions>),
                typeof(IMessageSerializer),
                typeof(IEnumerable<IMessageMiddleware>),
                typeof(ILogger<RabbitMQProvider>)
            ]);

        // Assert
        Assert.IsNotNull(coreConfigurationOverload.GetCustomAttribute<ObsoleteAttribute>());
        Assert.IsNotNull(rabbitConfigurationOverload.GetCustomAttribute<ObsoleteAttribute>());
        Assert.IsNotNull(singletonOverload.GetCustomAttribute<ObsoleteAttribute>());
        Assert.IsNotNull(aggregateHealthConstructor);
        Assert.IsNotNull(aggregateHealthConstructor.GetCustomAttribute<ObsoleteAttribute>());
        Assert.IsNotNull(telemetryMiddlewareConstructor);
        Assert.IsNotNull(telemetryMiddlewareConstructor.GetCustomAttribute<ObsoleteAttribute>());
        Assert.IsNotNull(rabbitProviderConstructor);
        Assert.IsNotNull(rabbitProviderConstructor.GetCustomAttribute<ObsoleteAttribute>());
    }

    [TestMethod]
    public void LegacyConfigurationOverloads_ConfigurationProvided_BindAndValidateOptions()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MessageQueue:DefaultTimeout"] = "00:00:09",
                ["MessageQueue:RabbitMQ:Host"] = "rabbitmq",
                ["MessageQueue:RabbitMQ:VirtualHost"] = "/",
                ["MessageQueue:RabbitMQ:Username"] = "useradmin",
                ["MessageQueue:RabbitMQ:Password"] = "passwordadmin"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
#pragma warning disable CS0618 // Exercising supported 8.x compatibility bridges.
        services.AddMessageQueue(configuration).AddRabbitMQ(configuration);
#pragma warning restore CS0618
        using var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.AreEqual(
            TimeSpan.FromSeconds(9),
            serviceProvider.GetRequiredService<IOptions<MessageQueueOptions>>().Value.DefaultTimeout);
        Assert.AreEqual(
            "rabbitmq",
            serviceProvider.GetRequiredService<IOptions<RabbitMQOptions>>().Value.Host);
    }

    [TestMethod]
    public async Task AddSingletonMessageQueue_QueueCreationPending_DoesNotBlockServiceResolution()
    {
        // Arrange
        var services = new ServiceCollection();
        await using var factory = new DelayedFactory();
        services.AddOptions<MessageQueueOptions>().Configure(options =>
            options.Providers["deferred"] = new ProviderConfiguration
            {
                Enabled = true,
                Type = "Delayed"
            });
        services.AddSingleton<IMessageQueueFactory>(factory);
#pragma warning disable CS0618 // Exercising supported 8.x compatibility bridge.
        services.AddSingletonMessageQueue("deferred");
#pragma warning restore CS0618
        await using var serviceProvider = services.BuildServiceProvider();

        // Act
        var queue = serviceProvider.GetRequiredService<IMessageQueue>();

        // Assert
        Assert.AreEqual(0, factory.CreateCallCount);
        Assert.AreEqual("deferred", queue.Name);
        Assert.AreEqual("Delayed", queue.Provider);

        var publishTask = queue.PublishAsync("orders", new BlockingMessage());
        await factory.CreationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(publishTask.IsCompleted);

        await using var publishingQueue = new PublishingQueue("deferred");
        factory.Complete(publishingQueue);
        Assert.AreEqual("published", await publishTask);
    }

    [TestMethod]
    public async Task PublishAsync_DefaultTimeoutExpires_ThrowsTimeoutException()
    {
        // Arrange
        await using var serviceProvider = CreateBlockingProvider(TimeSpan.FromMilliseconds(50));
        var factory = serviceProvider.GetRequiredService<IMessageQueueFactory>();
        var queue = await factory.CreateQueueAsync("blocking");

        // Act
        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => queue.PublishAsync("orders", new BlockingMessage()));

        // Assert
        StringAssert.Contains(exception.Message, "publish", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task PublishAsync_PerCallTimeoutExpires_OverridesDefaultTimeout()
    {
        // Arrange
        await using var serviceProvider = CreateBlockingProvider(TimeSpan.FromSeconds(5));
        var factory = serviceProvider.GetRequiredService<IMessageQueueFactory>();
        var queue = await factory.CreateQueueAsync("blocking");

        // Act
        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            queue.PublishAsync(
                "orders",
                new BlockingMessage(),
                new PublishOptions { Timeout = TimeSpan.FromMilliseconds(50) }));

        // Assert
        StringAssert.Contains(exception.Message, "00:00:00.0500000", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task PublishAsync_CallerCancellationRequested_PreservesOperationCancellation()
    {
        // Arrange
        await using var serviceProvider = CreateBlockingProvider(TimeSpan.FromSeconds(5));
        var factory = serviceProvider.GetRequiredService<IMessageQueueFactory>();
        var queue = await factory.CreateQueueAsync("blocking");
        using var callerCancellation = new CancellationTokenSource();
        await callerCancellation.CancelAsync();

        // Act
        var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            queue.PublishAsync(
                "orders",
                new BlockingMessage(),
                cancellationToken: callerCancellation.Token));

        // Assert
        Assert.AreEqual(callerCancellation.Token, exception.CancellationToken);
    }

    [TestMethod]
    public async Task To_DefaultTimeoutExpires_ThrowsTimeoutException()
    {
        // Arrange
        await using var serviceProvider = CreateBlockingProvider(TimeSpan.FromMilliseconds(50));
        var factory = serviceProvider.GetRequiredService<IMessageQueueFactory>();
        var queue = await factory.CreateQueueAsync("blocking");

        // Act
        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            queue.To<BlockingMessage>("orders").SendAsync(new BlockingMessage()));

        // Assert
        StringAssert.Contains(exception.Message, "publish", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task SubscribeAsync_SetupCompleted_ParentCancellationRemainsConnectedToProviderAndHandler()
    {
        // Arrange
        var provider = new LifetimeTokenProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(options =>
        {
            options.DefaultTimeout = TimeSpan.FromMilliseconds(10);
            options.Providers["lifetime"] = new ProviderConfiguration
            {
                Enabled = true,
                Type = LifetimeTokenProvider.ProviderType
            };
        });
        services.AddSingleton<IMessageQueueProvider>(provider);
        await using var serviceProvider = services.BuildServiceProvider();
        var queue = await serviceProvider
            .GetRequiredService<IMessageQueueFactory>()
            .CreateQueueAsync("lifetime");
        using var parentCancellation = new CancellationTokenSource();
        CancellationToken handlerToken = default;

        // Act
        await queue.SubscribeAsync<BlockingMessage>(
            "orders",
            context =>
            {
                handlerToken = context.CancellationToken;
                return Task.FromResult(MessageResult.Success());
            },
            cancellationToken: parentCancellation.Token);
        await parentCancellation.CancelAsync();
        await provider.Queue.DeliverAsync();

        // Assert
        Assert.AreEqual(parentCancellation.Token, provider.Queue.SubscriptionToken);
        Assert.IsTrue(provider.Queue.SubscriptionToken.IsCancellationRequested);
        Assert.AreEqual(parentCancellation.Token, handlerToken);
        Assert.IsTrue(handlerToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task CheckHealthAsync_ProviderTypeDiffersFromInstanceName_UsesEnabledInstanceName()
    {
        // Arrange
        await using var factory = new RecordingFactory();
        var options = Options.Create(new MessageQueueOptions
        {
            Providers = new Dictionary<string, ProviderConfiguration>
            {
                ["orders-primary"] = new() { Enabled = true, Type = "RabbitMQ" },
                ["disabled-secondary"] = new() { Enabled = false, Type = "RabbitMQ" }
            }
        });
        var healthCheck = new MessageQueueHealthCheck(factory, options);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.AreEqual(HealthStatus.Healthy, result.Status);
        CollectionAssert.AreEqual(_ordersPrimaryQueueName, factory.RequestedQueueNames.ToArray());
        Assert.IsTrue(result.Data.ContainsKey("orders-primary"));
        Assert.IsFalse(result.Data.ContainsKey("RabbitMQ"));
        Assert.IsFalse(result.Data.ContainsKey("disabled-secondary"));
    }

    [TestMethod]
    public async Task CheckHealthAsync_CallerCancellationRequested_PropagatesCancellation()
    {
        // Arrange
        var options = Options.Create(new MessageQueueOptions
        {
            Providers = new Dictionary<string, ProviderConfiguration>
            {
                ["blocking"] = new() { Enabled = true, Type = "RabbitMQ" }
            }
        });
        await using var factory = new BlockingFactory();
        var healthCheck = new MessageQueueHealthCheck(factory, options);
        using var callerCancellation = new CancellationTokenSource();
        await callerCancellation.CancelAsync();

        // Act
        var exception = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            healthCheck.CheckHealthAsync(new HealthCheckContext(), callerCancellation.Token));

        // Assert
        Assert.AreEqual(callerCancellation.Token, exception.CancellationToken);
    }

    [TestMethod]
    public async Task CheckHealthAsync_HealthChecksDisabled_DoesNotCreateConfiguredQueue()
    {
        // Arrange
        await using var factory = new RecordingFactory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageQueueFactory>(factory);
        services.AddMessageQueue(options =>
        {
            options.EnableHealthChecks = false;
            options.Providers["orders-primary"] = new ProviderConfiguration
            {
                Enabled = true,
                Type = "RabbitMQ"
            };
        });
        services.AddHealthChecks().AddMessageQueueHealthCheck();
        await using var serviceProvider = services.BuildServiceProvider();

        // Act
        var report = await serviceProvider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync();
        var result = report.Entries["messagequeue"];

        // Assert
        Assert.AreEqual(HealthStatus.Healthy, result.Status);
        CollectionAssert.AreEqual(Array.Empty<string>(), factory.RequestedQueueNames.ToArray());
        StringAssert.Contains(result.Description, "disabled", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task LegacyAggregateHealthConstructor_ConfiguredInstanceNamesDiffer_UsesInstanceNames()
    {
        // Arrange
        var provider = new RecordingProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(options =>
        {
            options.Providers["orders-primary"] = new ProviderConfiguration
            {
                Enabled = true,
                Type = RecordingProvider.ProviderType
            };
            options.Providers["disabled-secondary"] = new ProviderConfiguration
            {
                Enabled = false,
                Type = RecordingProvider.ProviderType
            };
        });
        services.AddSingleton<IMessageQueueProvider>(provider);
        await using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IMessageQueueFactory>();
#pragma warning disable CS0618 // Exercising supported 8.x compatibility constructor.
        var healthCheck = new MessageQueueHealthCheck(factory);
#pragma warning restore CS0618

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.AreEqual(HealthStatus.Healthy, result.Status);
        CollectionAssert.AreEqual(_ordersPrimaryQueueName, provider.RequestedQueueNames.ToArray());
        Assert.IsTrue(result.Data.ContainsKey("orders-primary"));
        Assert.IsFalse(result.Data.ContainsKey(RecordingProvider.ProviderType));
    }

    private static ServiceProvider CreateBlockingProvider(TimeSpan defaultTimeout)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(options =>
        {
            options.DefaultTimeout = defaultTimeout;
            options.Providers["blocking"] = new ProviderConfiguration
            {
                Enabled = true,
                Type = BlockingProvider.ProviderType
            };
        });
        services.AddSingleton<IMessageQueueProvider>(new BlockingProvider());
        return services.BuildServiceProvider();
    }

    private sealed class BlockingProvider : IMessageQueueProvider
    {
        public const string ProviderType = "Blocking";

        public string ProviderName => ProviderType;

        public ProviderCapabilities Capabilities { get; } = new();

        public Task<IMessageQueue> CreateAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IMessageQueue>(new BlockingQueue(name));
        }
    }

    private sealed class BlockingQueue : IMessageQueue
    {
        public BlockingQueue(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Provider => BlockingProvider.ProviderType;

        public Task<string> PublishAsync<TMessage>(
            string topic,
            TMessage message,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return BlockAsync<string>(cancellationToken);
        }

        public Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
            string topic,
            IEnumerable<TMessage> messages,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return BlockAsync<IReadOnlyList<string>>(cancellationToken);
        }

        public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic)
            where TMessage : class
        {
            throw new NotSupportedException();
        }

        public Task<IMessageSubscription> SubscribeAsync<TMessage>(
            string topic,
            Func<MessageContext<TMessage>, Task<MessageResult>> handler,
            SubscribeOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return BlockAsync<IMessageSubscription>(cancellationToken);
        }

        public Task<IMessageSubscription> SubscribeAsync<TMessage>(
            IEnumerable<string> topics,
            Func<MessageContext<TMessage>, Task<MessageResult>> handler,
            SubscribeOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class
        {
            return BlockAsync<IMessageSubscription>(cancellationToken);
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            return BlockAsync<bool>(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private static async Task<TResult> BlockAsync<TResult>(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("An infinite delay completed without cancellation.");
        }
    }

    private sealed class RecordingFactory : IMessageQueueFactory
    {
        public List<string> RequestedQueueNames { get; } = new();

        public Task<IMessageQueue> CreateQueueAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedQueueNames.Add(name);
            return Task.FromResult<IMessageQueue>(new HealthyQueue(name));
        }

        public IReadOnlyList<string> GetRegisteredProviders()
        {
            return new[] { "RabbitMQ" };
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingFactory : IMessageQueueFactory
    {
        public async Task<IMessageQueue> CreateQueueAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("An infinite delay completed without cancellation.");
        }

        public IReadOnlyList<string> GetRegisteredProviders()
        {
            return new[] { "RabbitMQ" };
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HealthyQueue : IMessageQueue
    {
        public HealthyQueue(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Provider => "RabbitMQ";

        public Task<string> PublishAsync<TMessage>(
            string topic,
            TMessage message,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
            string topic,
            IEnumerable<TMessage> messages,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => throw new NotSupportedException();

        public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic)
            where TMessage : class => throw new NotSupportedException();

        public Task<IMessageSubscription> SubscribeAsync<TMessage>(
            string topic,
            Func<MessageContext<TMessage>, Task<MessageResult>> handler,
            SubscribeOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => throw new NotSupportedException();

        public Task<IMessageSubscription> SubscribeAsync<TMessage>(
            IEnumerable<string> topics,
            Func<MessageContext<TMessage>, Task<MessageResult>> handler,
            SubscribeOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => throw new NotSupportedException();

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DelayedFactory : IMessageQueueFactory
    {
        private readonly TaskCompletionSource<IMessageQueue> _queueCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _createCallCount;

        public TaskCompletionSource CreationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CreateCallCount => Volatile.Read(ref _createCallCount);

        public async Task<IMessageQueue> CreateQueueAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCallCount);
            CreationStarted.TrySetResult();
            return await _queueCompletion.Task.WaitAsync(cancellationToken);
        }

        public IReadOnlyList<string> GetRegisteredProviders() => ["Delayed"];

        public void Complete(IMessageQueue queue) => _queueCompletion.TrySetResult(queue);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PublishingQueue : IMessageQueue
    {
        public PublishingQueue(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Provider => "Delayed";

        public Task<string> PublishAsync<TMessage>(
            string topic,
            TMessage message,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => Task.FromResult("published");

        public Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
            string topic,
            IEnumerable<TMessage> messages,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => Task.FromResult<IReadOnlyList<string>>(["published"]);

        public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic)
            where TMessage : class => throw new NotSupportedException();

        public Task<IMessageSubscription> SubscribeAsync<TMessage>(
            string topic,
            Func<MessageContext<TMessage>, Task<MessageResult>> handler,
            SubscribeOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => throw new NotSupportedException();

        public Task<IMessageSubscription> SubscribeAsync<TMessage>(
            IEnumerable<string> topics,
            Func<MessageContext<TMessage>, Task<MessageResult>> handler,
            SubscribeOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => throw new NotSupportedException();

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LifetimeTokenProvider : IMessageQueueProvider
    {
        public const string ProviderType = "LifetimeToken";

        public LifetimeTokenQueue Queue { get; } = new();

        public string ProviderName => ProviderType;

        public ProviderCapabilities Capabilities { get; } = new();

        public Task<IMessageQueue> CreateAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IMessageQueue>(Queue);
    }

    private sealed class LifetimeTokenQueue : IMessageQueue
    {
        private Func<Task>? _deliver;

        public string Name => "lifetime";

        public string Provider => LifetimeTokenProvider.ProviderType;

        public CancellationToken SubscriptionToken { get; private set; }

        public Task<string> PublishAsync<TMessage>(
            string topic,
            TMessage message,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
            string topic,
            IEnumerable<TMessage> messages,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class => throw new NotSupportedException();

        public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic)
            where TMessage : class => throw new NotSupportedException();

        public Task<IMessageSubscription> SubscribeAsync<TMessage>(
            string topic,
            Func<MessageContext<TMessage>, Task<MessageResult>> handler,
            SubscribeOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class
        {
            SubscriptionToken = cancellationToken;
            _deliver = () => handler(new MessageContext<TMessage>
            {
                Envelope = new MessageEnvelope<TMessage>
                {
                    MessageId = "lifetime-message",
                    MessageType = typeof(TMessage).FullName ?? typeof(TMessage).Name,
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = null!
                },
                Topic = topic,
                CancellationToken = SubscriptionToken
            });
            return Task.FromResult<IMessageSubscription>(new TestSubscription(topic));
        }

        public Task<IMessageSubscription> SubscribeAsync<TMessage>(
            IEnumerable<string> topics,
            Func<MessageContext<TMessage>, Task<MessageResult>> handler,
            SubscribeOptions? options = null,
            CancellationToken cancellationToken = default)
            where TMessage : class =>
            SubscribeAsync(topics.First(), handler, options, cancellationToken);

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task DeliverAsync() => _deliver?.Invoke()
            ?? throw new InvalidOperationException("No handler has been registered.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestSubscription(string topic) : IMessageSubscription
    {
        public string SubscriptionId { get; } = Guid.NewGuid().ToString();

        public IReadOnlyList<string> Topics { get; } = [topic];

        public bool IsActive => true;

        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingProvider : IMessageQueueProvider
    {
        public const string ProviderType = "Recording";

        public List<string> RequestedQueueNames { get; } = new();

        public string ProviderName => ProviderType;

        public ProviderCapabilities Capabilities { get; } = new();

        public Task<IMessageQueue> CreateAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedQueueNames.Add(name);
            return Task.FromResult<IMessageQueue>(new HealthyQueue(name));
        }
    }

    private sealed record BlockingMessage;
}
