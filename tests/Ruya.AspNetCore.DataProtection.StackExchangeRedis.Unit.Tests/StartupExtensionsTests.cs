using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Ruya.Diagnostics.DistributedTracing;

using StackExchange.Redis;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis.Unit.Tests;

[TestClass]
public class StartupExtensionsTests
{
    [TestMethod]
    public void AddDataProtectionServer_NullServices_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            StartupExtensions.AddDataProtectionServer(null!));
    }

    [TestMethod]
    public void AddDataProtectionServer_BeforeRequiredDependencies_RegistrationSucceeds()
    {
        var services = new ServiceCollection();

        var result = services.AddDataProtectionServer();
        AddRequiredDependencies(services);

        Assert.AreSame(services, result);
    }

    [TestMethod]
    public void AddDataProtectionServer_WithConfigureAction_DefersActionUntilOptionsResolution()
    {
        var services = CreateServicesWithDependencies();
        var actionCalled = false;

        services.AddDataProtectionServer(settings =>
        {
            actionCalled = true;
            settings.DefaultKeyLifetime = 30;
        });

        Assert.IsFalse(actionCalled);
    }

    [TestMethod]
    public void AddDataProtectionServer_OptionsResolved_ExposesResolvedConnectionForRemoteClients()
    {
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateServerConfiguration());
        services.AddDataProtectionServer();
        using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<IOptions<DataProtectionSettings>>().Value;

        Assert.AreEqual("localhost:6379", settings.ConnectionString);
    }

    [TestMethod]
    public void AddDataProtectionServer_ReferencedConnectionMissing_ThrowsOptionsValidationException()
    {
        var services = CreateServicesWithDependencies();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtectionSettings:ApplicationName"] = "tests",
                ["DataProtectionSettings:ConnectionStringKey"] = "Redis",
                ["DataProtectionSettings:CacheKey"] = "keys"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDataProtectionServer();
        using var provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<DataProtectionSettings>>().Value);
    }

    [TestMethod]
    public void AddDataProtectionServer_BlankConfiguredPurpose_ThrowsOptionsValidationException()
    {
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateServerConfiguration());
        services.AddDataProtectionServer(settings =>
            settings.Purposes[DataProtectionService.DefaultPurpose] = " ");
        using var provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<DataProtectionSettings>>().Value);
    }

    [TestMethod]
    public void AddDataProtectionClient_NullServices_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            StartupExtensions.AddDataProtectionClient(null!, "test-purpose"));
    }

    [TestMethod]
    public void AddDataProtectionClient_NullDefaultPurpose_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            services.AddDataProtectionClient(null!));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AddDataProtectionClient_BlankDefaultPurpose_ThrowsArgumentException(string defaultPurpose)
    {
        var services = new ServiceCollection();

        Assert.ThrowsExactly<ArgumentException>(() =>
            services.AddDataProtectionClient(defaultPurpose));
    }

    [TestMethod]
    public void AddDataProtectionClient_BeforeRequiredDependencies_RegistrationSucceeds()
    {
        var services = new ServiceCollection();

        var result = services.AddDataProtectionClient("test-purpose");
        AddRequiredDependencies(services);

        Assert.AreSame(services, result);
    }

    [TestMethod]
    [DataRow("http://config.example.test", "/api/DataProtection")]
    [DataRow("https://config.example.test", "https://other.example.test/api/DataProtection")]
    public void AddDataProtectionClient_UnsafeRemoteEndpoint_ThrowsOptionsValidationException(
        string serviceAddress,
        string endpoint)
    {
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration(serviceAddress, endpoint));
        services.AddDataProtectionClient("maui.default");
        using var provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<DataProtectionClientSettings>>().Value);
    }

    [TestMethod]
    public void AddDataProtectionClient_UserInfoInRemoteAddress_RejectsWithoutExposingCredential()
    {
        const string credential = "uri-secret-sentinel";
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration(
            $"https://device:{credential}@config.example.test"));
        services.AddDataProtectionClient("maui.default");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<DataProtectionClientSettings>>().Value);

        Assert.DoesNotContain(credential, exception.ToString(), StringComparison.Ordinal);
    }

    [TestMethod]
    public void AddDataProtectionClient_LoopbackHttpEndpoint_ResolvesValidatedOptions()
    {
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration("http://localhost:5050"));
        services.AddDataProtectionClient("maui.default");
        using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<IOptions<DataProtectionClientSettings>>().Value;

        Assert.AreEqual("http://localhost:5050", settings.ConnectionString);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_RemoteOnlyConfiguration_InitializesOnceAndSharesConnection()
    {
        const string redisConnection = "redis.example.test:6379,password=runtime-secret";
        var remoteJson = CreateRemoteSettingsJson(redisConnection);
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(remoteJson));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var connectionFactory = new StubRedisConnectionFactory(multiplexer.Object);
        var callbackCount = 0;

        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(connectionFactory);
        services.AddDataProtectionClient("maui.default", settings =>
        {
            Interlocked.Increment(ref callbackCount);
            settings.DefaultKeyLifetime = 31;
        });
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName, client =>
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "device-token"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => provider.InitializeDataProtectionClientAsync()));

        var settings = provider.GetRequiredService<IOptions<DataProtectionSettings>>().Value;
        var firstConnection = provider.GetRequiredService<IConnectionMultiplexer>();
        var secondConnection = provider.GetRequiredService<IConnectionMultiplexer>();
        var dataProtectionOptions = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        var keyManagementOptions = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual("https://config.example.test/api/DataProtection", handler.LastRequestUri?.ToString());
        Assert.AreEqual("Bearer device-token", handler.LastAuthorization);
        Assert.AreEqual(1, callbackCount);
        Assert.AreEqual(redisConnection, settings.ConnectionString);
        Assert.AreEqual("maui.default", settings.Purposes[DataProtectionService.DefaultPurpose]);
        Assert.AreEqual(31, settings.DefaultKeyLifetime);
        Assert.AreSame(multiplexer.Object, firstConnection);
        Assert.AreSame(firstConnection, secondConnection);
        Assert.AreEqual("remote-client", dataProtectionOptions.ApplicationDiscriminator);
        Assert.AreEqual(TimeSpan.FromDays(31), keyManagementOptions.NewKeyLifetime);
        Assert.IsNotNull(keyManagementOptions.XmlRepository);
        Assert.AreEqual(1, connectionFactory.AsyncConnectCount);
        Assert.AreEqual(redisConnection, connectionFactory.LastConnectionString);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_InvalidRemoteSettings_FailsWithoutExposingCredential()
    {
        const string credential = "redis.example.test:6379,password=do-not-expose";
        var invalidJson = JsonSerializer.Serialize(new
        {
            applicationName = "remote-client",
            defaultKeyLifetime = 90,
            connectionStringKey = "Redis",
            connectionString = credential,
            cacheKey = ""
        });
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(invalidJson));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(new StubRedisConnectionFactory(multiplexer.Object));
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => provider.InitializeDataProtectionClientAsync());

        Assert.DoesNotContain("do-not-expose", exception.ToString(), StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_InvalidPayloadThenValidPayload_RetriesInitialization()
    {
        var responseCount = 0;
        using var handler = new RecordingHttpMessageHandler(_ =>
        {
            var responseNumber = Interlocked.Increment(ref responseCount);
            var json = responseNumber == 1
                ? JsonSerializer.Serialize(new
                {
                    applicationName = "remote-client",
                    defaultKeyLifetime = 90,
                    connectionStringKey = "Redis",
                    connectionString = "localhost:6379",
                    cacheKey = ""
                })
                : CreateRemoteSettingsJson("localhost:6379");
            return CreateJsonResponse(json);
        });
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var connectionFactory = new StubRedisConnectionFactory(multiplexer.Object);
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(connectionFactory);
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => provider.InitializeDataProtectionClientAsync());
        await provider.InitializeDataProtectionClientAsync();

        Assert.AreEqual(2, handler.RequestCount);
        Assert.AreEqual(1, connectionFactory.AsyncConnectCount);
    }

    [TestMethod]
    public void AddDataProtectionClient_NoExplicitPrewarm_SynchronousCompatibilityProjectionInitializesOnce()
    {
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(
            CreateRemoteSettingsJson("localhost:6379")));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var connectionFactory = new StubRedisConnectionFactory(multiplexer.Object);
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(connectionFactory);
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<IOptions<DataProtectionSettings>>().Value;
        var firstConnection = provider.GetRequiredService<IConnectionMultiplexer>();
        var secondConnection = provider.GetRequiredService<IConnectionMultiplexer>();

        Assert.AreEqual("remote-client", settings.ApplicationName);
        Assert.AreSame(multiplexer.Object, firstConnection);
        Assert.AreSame(firstConnection, secondConnection);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(1, connectionFactory.AsyncConnectCount);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_RedisFailure_DoesNotExposeDependencyMessage()
    {
        const string credential = "redis-secret-sentinel";
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(
            CreateRemoteSettingsJson("localhost:6379")));
        var logger = new RecordingLogger<DataProtectionService>();
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<ILogger<DataProtectionService>>(logger);
        services.AddSingleton<IRedisConnectionFactory>(new ThrowingRedisConnectionFactory(credential));
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => provider.InitializeDataProtectionClientAsync());

        Assert.DoesNotContain(credential, exception.ToString(), StringComparison.Ordinal);
        Assert.IsTrue(logger.Messages.All(message =>
            !message.Contains(credential, StringComparison.Ordinal)));
        Assert.IsTrue(logger.Exceptions.All(loggedException => loggedException is null));
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_PreCanceledToken_DoesNotStartRemoteFetch()
    {
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(
            CreateRemoteSettingsJson("localhost:6379")));
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => provider.InitializeDataProtectionClientAsync(cancellationSource.Token));

        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_CanceledWaitThenRemoteCompletes_NextWaitSucceeds()
    {
        using var handler = new DeferredHttpMessageHandler();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var connectionFactory = new StubRedisConnectionFactory(multiplexer.Object);
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(connectionFactory);
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var cancellationSource = new CancellationTokenSource();

        var canceledWait = provider.InitializeDataProtectionClientAsync(cancellationSource.Token);
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationSource.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => canceledWait);

        using var response = CreateJsonResponse(CreateRemoteSettingsJson("localhost:6379"));
        handler.Complete(response);
        await provider.InitializeDataProtectionClientAsync();

        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(1, connectionFactory.AsyncConnectCount);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_NoLaterConnectionConsumer_RootProviderOwnsConnection()
    {
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(
            CreateRemoteSettingsJson("localhost:6379")));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(new StubRedisConnectionFactory(multiplexer.Object));
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();

        await provider.InitializeDataProtectionClientAsync();
        await provider.DisposeAsync();

        multiplexer.As<IAsyncDisposable>()
            .Verify(connection => connection.DisposeAsync(), Times.Once);
        multiplexer.Verify(connection => connection.Dispose(), Times.Never);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_CanceledDuringRedisConnectThenProviderDisposed_EventualConnectionIsDisposed()
    {
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(
            CreateRemoteSettingsJson("localhost:6379")));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var connectionFactory = new DeferredRedisConnectionFactory();
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(connectionFactory);
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        using var cancellationSource = new CancellationTokenSource();

        var initialization = provider.InitializeDataProtectionClientAsync(cancellationSource.Token);
        await connectionFactory.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationSource.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => initialization);

        var disposal = provider.DisposeAsync().AsTask();
        Assert.IsFalse(disposal.IsCompleted);
        connectionFactory.Complete(multiplexer.Object);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, connectionFactory.AsyncConnectCount);
        multiplexer.As<IAsyncDisposable>()
            .Verify(connection => connection.DisposeAsync(), Times.Once);
        multiplexer.Verify(connection => connection.Dispose(), Times.Never);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_CanceledDuringRetriedRedisConnectThenProviderDisposed_EventualConnectionIsDisposed()
    {
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(
            CreateRemoteSettingsJson("localhost:6379")));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        var connectionFactory = new FailingThenDeferredRedisConnectionFactory();
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(connectionFactory);
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => provider.InitializeDataProtectionClientAsync());

        using var cancellationSource = new CancellationTokenSource();
        var retry = provider.InitializeDataProtectionClientAsync(cancellationSource.Token);
        await connectionFactory.RetryStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationSource.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => retry);

        var disposal = provider.DisposeAsync().AsTask();
        Assert.IsFalse(disposal.IsCompleted);
        connectionFactory.Complete(multiplexer.Object);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(2, connectionFactory.AsyncConnectCount);
        multiplexer.As<IAsyncDisposable>()
            .Verify(connection => connection.DisposeAsync(), Times.Once);
        multiplexer.Verify(connection => connection.Dispose(), Times.Never);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_LateConnectionDisposalFails_ProviderDisposalPropagatesFailure()
    {
        using var handler = new RecordingHttpMessageHandler(_ => CreateJsonResponse(
            CreateRemoteSettingsJson("localhost:6379")));
        var cleanupFailure = new InvalidOperationException("Redis cleanup failed.");
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.As<IAsyncDisposable>()
            .Setup(connection => connection.DisposeAsync())
            .Returns(() => ValueTask.FromException(cleanupFailure));
        var connectionFactory = new DeferredRedisConnectionFactory();
        var services = CreateServicesWithDependencies();
        services.AddSingleton<IConfiguration>(CreateClientConfiguration());
        services.AddSingleton<IRedisConnectionFactory>(connectionFactory);
        services.AddDataProtectionClient("maui.default");
        services.AddHttpClient(DataProtectionClientSettings.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        using var cancellationSource = new CancellationTokenSource();

        var initialization = provider.InitializeDataProtectionClientAsync(cancellationSource.Token);
        await connectionFactory.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationSource.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => initialization);

        var disposal = provider.DisposeAsync().AsTask();
        connectionFactory.Complete(multiplexer.Object);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => disposal);

        Assert.AreSame(cleanupFailure, exception);
        multiplexer.As<IAsyncDisposable>()
            .Verify(connection => connection.DisposeAsync(), Times.Once);
    }

    [TestMethod]
    public async Task InitializeDataProtectionClientAsync_ClientNotRegistered_ThrowsInvalidOperationException()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => provider.InitializeDataProtectionClientAsync());

        StringAssert.Contains(exception.Message, nameof(StartupExtensions.AddDataProtectionClient), StringComparison.Ordinal);
    }

    [TestMethod]
    public void AddDataProtectionClient_RegistersRemoteInitializationAndSingletonConnection()
    {
        var services = new ServiceCollection();

        services.AddDataProtectionClient("test-purpose");

        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(AsyncLazy<DataProtectionSettings>)));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(AsyncLazy<IConnectionMultiplexer>)));
        var connectionDescriptor = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IConnectionMultiplexer));
        Assert.IsNotNull(connectionDescriptor);
        Assert.AreEqual(ServiceLifetime.Singleton, connectionDescriptor.Lifetime);
    }

    [TestMethod]
    public void AddDataProtectionServer_RegistersHealthCheck()
    {
        var services = new ServiceCollection();

        services.AddDataProtectionServer();

        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<HealthCheckServiceOptions>)));
    }

    private static ServiceCollection CreateServicesWithDependencies()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        return services;
    }

    private static void AddRequiredDependencies(IServiceCollection services)
    {
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<IDistributedTracing>(new Mock<IDistributedTracing>().Object);
    }

    private static IConfiguration CreateServerConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtectionSettings:ApplicationName"] = "tests",
                ["DataProtectionSettings:ConnectionStringKey"] = "Redis",
                ["DataProtectionSettings:CacheKey"] = "keys",
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();

    private static IConfiguration CreateClientConfiguration(
        string serviceAddress = "https://config.example.test",
        string endpoint = "/api/DataProtection") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtectionClientSettings:ConnectionStringKey"] = "ConfigService",
                ["DataProtectionClientSettings:Endpoint"] = endpoint,
                ["ConnectionStrings:ConfigService"] = serviceAddress
            })
            .Build();

    private static string CreateRemoteSettingsJson(string connectionString) =>
        JsonSerializer.Serialize(new
        {
            applicationName = "remote-client",
            defaultKeyLifetime = 90,
            connectionStringKey = "Redis",
            connectionString,
            cacheKey = "data-protection-keys"
        });

    private static HttpResponseMessage CreateJsonResponse(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public Uri? LastRequestUri { get; private set; }

        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            LastRequestUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class DeferredHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public Task Started => _started.Task;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public void Complete(HttpResponseMessage response) => _response.TrySetResult(response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            _started.TrySetResult();
            return _response.Task;
        }
    }

    private sealed class StubRedisConnectionFactory(IConnectionMultiplexer connection) : IRedisConnectionFactory
    {
        private int _asyncConnectCount;

        public int AsyncConnectCount => Volatile.Read(ref _asyncConnectCount);

        public string? LastConnectionString { get; private set; }

        public IConnectionMultiplexer Connect(string connectionString)
        {
            LastConnectionString = connectionString;
            return connection;
        }

        public Task<IConnectionMultiplexer> ConnectAsync(string connectionString)
        {
            LastConnectionString = connectionString;
            Interlocked.Increment(ref _asyncConnectCount);
            return Task.FromResult(connection);
        }
    }

    private sealed class DeferredRedisConnectionFactory : IRedisConnectionFactory
    {
        private readonly TaskCompletionSource<IConnectionMultiplexer> _connection =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _asyncConnectCount;

        public Task Started => _started.Task;

        public int AsyncConnectCount => Volatile.Read(ref _asyncConnectCount);

        public void Complete(IConnectionMultiplexer connection) =>
            _connection.TrySetResult(connection);

        public IConnectionMultiplexer Connect(string connectionString) =>
            throw new NotSupportedException();

        public Task<IConnectionMultiplexer> ConnectAsync(string connectionString)
        {
            Interlocked.Increment(ref _asyncConnectCount);
            _started.TrySetResult();
            return _connection.Task;
        }
    }

    private sealed class FailingThenDeferredRedisConnectionFactory : IRedisConnectionFactory
    {
        private readonly TaskCompletionSource<IConnectionMultiplexer> _connection =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _retryStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _asyncConnectCount;

        public Task RetryStarted => _retryStarted.Task;

        public int AsyncConnectCount => Volatile.Read(ref _asyncConnectCount);

        public void Complete(IConnectionMultiplexer connection) =>
            _connection.TrySetResult(connection);

        public IConnectionMultiplexer Connect(string connectionString) =>
            throw new NotSupportedException();

        public Task<IConnectionMultiplexer> ConnectAsync(string connectionString)
        {
            var attempt = Interlocked.Increment(ref _asyncConnectCount);
            if (attempt == 1)
            {
                return Task.FromException<IConnectionMultiplexer>(
                    new InvalidOperationException("First connection attempt failed."));
            }

            _retryStarted.TrySetResult();
            return _connection.Task;
        }
    }

    private sealed class ThrowingRedisConnectionFactory(string message) : IRedisConnectionFactory
    {
        public IConnectionMultiplexer Connect(string connectionString) =>
            throw new InvalidOperationException(message);

        public Task<IConnectionMultiplexer> ConnectAsync(string connectionString) =>
            Task.FromException<IConnectionMultiplexer>(new InvalidOperationException(message));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
