using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Ruya.Services.TokenBroker.Client;
using Ruya.Services.DistributedLock.Abstractions;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class TokenBrokerConfigurationTests
{
    [TestMethod]
    public void AddTokenValidation_PublicKeyRing_PinsAlgorithmAndKeyId()
    {
        var services = CreateServices();
        services.AddLogging();
        services.AddTokenValidation(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningPublicKeys.Add(TestSigningKeys.KeyId, TestSigningKeys.PublicKeyPem);
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = options.TokenValidationParameters;

        CollectionAssert.Contains(parameters.ValidAlgorithms.ToArray(), SecurityAlgorithms.RsaSha256);
        Assert.HasCount(1, parameters.IssuerSigningKeyResolver!(string.Empty, null, TestSigningKeys.KeyId, parameters));
        Assert.IsEmpty(parameters.IssuerSigningKeyResolver!(string.Empty, null, "unknown-key", parameters));
    }

    [TestMethod]
    public void AddTokenValidation_LegacySymmetricValue_FailsClosedWithValidPublicKeyRing()
    {
        var services = CreateServices();
        services.AddLogging();
        services.AddTokenValidation(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningPublicKeys.Add(TestSigningKeys.KeyId, TestSigningKeys.PublicKeyPem);
#pragma warning disable CS0618 // Explicitly verifies the 8.x compatibility member fails closed.
            settings.SigningKeyBase64 = Convert.ToBase64String(new byte[32]);
#pragma warning restore CS0618
        });

        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenValidationSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenBroker_LegacySymmetricValue_FailsClosedWithValidPrivateKey()
    {
        var services = CreateServices();
        services.AddSingleton<IDistributedLock>(new PassThroughDistributedLock());
        services.AddTokenBroker(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningKeyId = TestSigningKeys.KeyId;
            settings.SigningPrivateKeyPem = TestSigningKeys.PrivateKeyPem;
            settings.SigningPublicKeys.Add(TestSigningKeys.KeyId, TestSigningKeys.PublicKeyPem);
#pragma warning disable CS0618 // Explicitly verifies the 8.x compatibility member fails closed.
            settings.SigningKeyBase64 = Convert.ToBase64String(new byte[32]);
#pragma warning restore CS0618
        });

        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenBrokerSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenValidation_PrivateKeyConfiguredAsValidatorKey_FailsClosed()
    {
        var services = CreateServices();
        services.AddLogging();
        services.AddTokenValidation(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningPublicKeys.Add(TestSigningKeys.KeyId, TestSigningKeys.PrivateKeyPem);
        });

        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenValidationSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenValidation_WeakRsaPublicKey_FailsClosed()
    {
        using var weakRsa = RSA.Create(1024);
        var services = CreateServices();
        services.AddLogging();
        services.AddTokenValidation(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningPublicKeys.Add("weak-key", weakRsa.ExportSubjectPublicKeyInfoPem());
        });

        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenValidationSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenBroker_WeakRsaPrivateKey_FailsClosed()
    {
        using var weakRsa = RSA.Create(1024);
        var services = CreateServices();
        services.AddSingleton<IDistributedLock>(new PassThroughDistributedLock());
        services.AddTokenBroker(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningKeyId = "weak-key";
            settings.SigningPrivateKeyPem = weakRsa.ExportPkcs8PrivateKeyPem();
            settings.SigningPublicKeys.Add("weak-key", weakRsa.ExportSubjectPublicKeyInfoPem());
        });

        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenBrokerSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenClient_RemoteHttpWithoutDevelopmentOverride_FailsClosed()
    {
        using var provider = BuildTokenClientProvider("http://token-service", allowInsecure: false);

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenClientSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenClient_RemoteHttpWithDevelopmentOverrideInProduction_FailsClosed()
    {
        using var provider = BuildTokenClientProvider(
            "http://token-service",
            allowInsecure: true,
            Environments.Production);

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenClientSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenClient_RemoteHttpWithOverrideAndNoHostEnvironment_FailsClosed()
    {
        var services = CreateServices();
        services.AddTokenClient(settings =>
        {
            settings.TokenBrokerUrl = "http://token-service";
            settings.ServiceName = "test-service";
            settings.ApiKey = "test-api-key-1234567890";
            settings.AllowInsecureHttpForDevelopment = true;
        });
        using var provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenClientSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenClient_RemoteHttpWithDevelopmentOverrideInDevelopment_PassesValidation()
    {
        using var provider = BuildTokenClientProvider(
            "http://token-service",
            allowInsecure: true,
            Environments.Development);

        var settings = provider.GetRequiredService<IOptions<TokenClientSettings>>().Value;

        Assert.AreEqual("http://token-service", settings.TokenBrokerUrl);
    }

    [TestMethod]
    public void AddTokenClient_HttpsEndpoint_PassesValidation()
    {
        using var provider = BuildTokenClientProvider("https://token-service", allowInsecure: false);

        var settings = provider.GetRequiredService<IOptions<TokenClientSettings>>().Value;

        Assert.AreEqual("https://token-service", settings.TokenBrokerUrl);
    }

    [TestMethod]
    public void AddTokenClient_FtpLoopbackEndpoint_FailsClosed()
    {
        using var provider = BuildTokenClientProvider("ftp://localhost", allowInsecure: true);

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenClientSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenBroker_MissingDistributedLock_FailsDuringOptionsActivation()
    {
        var services = CreateServices();
        services.AddTokenBroker(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningKeyId = TestSigningKeys.KeyId;
            settings.SigningPrivateKeyPem = TestSigningKeys.PrivateKeyPem;
            settings.SigningPublicKeys.Add(TestSigningKeys.KeyId, TestSigningKeys.PublicKeyPem);
        });

        using var provider = services.BuildServiceProvider();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenBrokerSettings>>().Value);
    }

    [TestMethod]
    public void AddTokenBroker_DistributedLockAndRsaSettings_PassValidation()
    {
        var services = CreateServices();
        services.AddSingleton<IDistributedLock>(new PassThroughDistributedLock());
        services.AddTokenBroker(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningKeyId = TestSigningKeys.KeyId;
            settings.SigningPrivateKeyPem = TestSigningKeys.PrivateKeyPem;
            settings.SigningPublicKeys.Add(TestSigningKeys.KeyId, TestSigningKeys.PublicKeyPem);
        });

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IOptions<TokenBrokerSettings>>().Value;

        Assert.AreEqual(TestSigningKeys.KeyId, settings.SigningKeyId);
    }

    [TestMethod]
    public void AddTokenBroker_CurrentPublicKeyDoesNotMatchPrivateKey_FailsClosed()
    {
        var services = CreateServices();
        services.AddSingleton<IDistributedLock>(new PassThroughDistributedLock());
        services.AddTokenBroker(settings =>
        {
            settings.Issuer = "test-issuer";
            settings.Audiences.Add("test-audience");
            settings.SigningKeyId = TestSigningKeys.KeyId;
            settings.SigningPrivateKeyPem = TestSigningKeys.PrivateKeyPem;
            settings.SigningPublicKeys.Add(TestSigningKeys.KeyId, TestSigningKeys.PreviousPublicKeyPem);
        });

        using var provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<TokenBrokerSettings>>().Value);
    }

    [TestMethod]
    public async Task AddTokenClient_UnsafePostReturnsTransientFailure_DoesNotRetry()
    {
        var handler = new CountingFailureHandler();
        var services = CreateServices();
        services.AddMetrics();
        services.AddTokenClient(
            settings =>
            {
                settings.TokenBrokerUrl = "https://token-service";
                settings.ServiceName = "test-service";
                settings.ApiKey = "test-api-key-1234567890";
            },
            resilience =>
            {
                resilience.Retry.Delay = TimeSpan.Zero;
                resilience.Retry.UseJitter = false;
            });
        services.AddHttpClient<ITokenClient, TokenClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITokenClient>();

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetTokenAsync());

        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    public async Task AddTokenClient_TransportFailure_DoesNotAttachExceptionOrSensitiveMessageToLog()
    {
        const string sensitiveMessage = "sentinel-client-secret";
        var handler = new ThrowingHandler(sensitiveMessage);
        var logger = new CapturingLogger<TokenClient>();
        var services = CreateServices();
        services.AddMetrics();
        services.AddSingleton<ILogger<TokenClient>>(logger);
        services.AddTokenClient(settings =>
        {
            settings.TokenBrokerUrl = "https://token-service";
            settings.ServiceName = "test-service";
            settings.ApiKey = "test-api-key-1234567890";
        });
        services.AddHttpClient<ITokenClient, TokenClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITokenClient>();

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetTokenAsync());

        Assert.HasCount(1, logger.Entries);
        var entry = logger.Entries[0];
        Assert.IsNull(entry.Exception);
        StringAssert.Contains(entry.Message, nameof(HttpRequestException));
        Assert.IsFalse(entry.Message.Contains(sensitiveMessage, StringComparison.Ordinal));
    }

    // This literal pins the external host-environment contract independently of CLR field renames.
    private static ServiceProvider BuildTokenClientProvider(
        string url,
        bool allowInsecure,
        string environmentName = "Production")
    {
        var services = CreateServices();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services.AddTokenClient(new Action<TokenClientSettings>(settings =>
        {
            settings.TokenBrokerUrl = url;
            settings.ServiceName = "test-service";
            settings.ApiKey = "test-api-key-1234567890";
            settings.AllowInsecureHttpForDevelopment = allowInsecure;
        }));
        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = nameof(TokenBrokerConfigurationTests);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        return services;
    }

    private sealed class CountingFailureHandler : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                RequestMessage = request
            });
        }
    }

    private sealed class ThrowingHandler(string message) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new HttpRequestException(message);
    }
}
