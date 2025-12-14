using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class ApiKeyValidatorTests
{
    private Mock<ILogger<ApiKeyValidator>> _loggerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;
    private Mock<IOptions<TokenBrokerSettings>> _optionsMock = null!;
    private Mock<IDistributedCache> _cacheMock = null!;
    private TokenBrokerSettings _settings = null!;
    private ApiKeyValidator _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<ApiKeyValidator>>();
        _meterFactoryMock = new Mock<IMeterFactory>();
        _optionsMock = new Mock<IOptions<TokenBrokerSettings>>();
        _cacheMock = new Mock<IDistributedCache>();

        _settings = new TokenBrokerSettings
        {
            Issuer = "test-issuer",
            Audiences = ["test-audience"],
            SigningKeyBase64 = Convert.ToBase64String(new byte[32]),
            ApiKeyCacheDuration = TimeSpan.FromMinutes(5)
        };

        _optionsMock.Setup(o => o.Value).Returns(_settings);

        var meter = new Meter("Ruya.TokenBroker", "1.0.0");
        _meterFactoryMock.Setup(m => m.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        _sut = new ApiKeyValidator(
            _loggerMock.Object,
            _meterFactoryMock.Object,
            _optionsMock.Object,
            _cacheMock.Object);
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_ValidKey_ReturnsRegistration()
    {
        // Arrange
        var apiKey = "valid-api-key";
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash",
            AllowedScopes = ["scope1"]
        };
        var json = JsonSerializer.Serialize(registration, Constants.JsonSerializerOptions);

        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        // Act
        var result = await _sut.ValidateApiKeyAsync(apiKey);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(registration.ServiceName, result.ServiceName);
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_InvalidKey_ReturnsNull()
    {
        // Arrange
        var apiKey = "invalid-api-key";
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        var result = await _sut.ValidateApiKeyAsync(apiKey);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_NullApiKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.ValidateApiKeyAsync(null!));
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_EmptyApiKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _sut.ValidateApiKeyAsync(string.Empty));
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_WhitespaceApiKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _sut.ValidateApiKeyAsync("   "));
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_InvalidJson_ReturnsNull()
    {
        // Arrange
        var apiKey = "test-api-key";
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("invalid-json"));

        // Act & Assert
        await Assert.ThrowsExactlyAsync<JsonException>(
            () => _sut.ValidateApiKeyAsync(apiKey));
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_WithAllowedScopes_ReturnsAllowedScopes()
    {
        // Arrange
        var apiKey = "test-api-key";
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash",
            AllowedScopes = ["scope1", "scope2", "scope3"]
        };
        var json = JsonSerializer.Serialize(registration, Constants.JsonSerializerOptions);

        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        // Act
        var result = await _sut.ValidateApiKeyAsync(apiKey);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.AllowedScopes);
        Assert.AreEqual(3, result.AllowedScopes.Count);
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_WithCanExchangeTokensTrue_ReturnsCorrectValue()
    {
        // Arrange
        var apiKey = "test-api-key";
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash",
            CanExchangeTokens = true
        };
        var json = JsonSerializer.Serialize(registration, Constants.JsonSerializerOptions);

        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        // Act
        var result = await _sut.ValidateApiKeyAsync(apiKey);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.CanExchangeTokens);
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_WithCanExchangeTokensFalse_ReturnsCorrectValue()
    {
        // Arrange
        var apiKey = "test-api-key";
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash",
            CanExchangeTokens = false
        };
        var json = JsonSerializer.Serialize(registration, Constants.JsonSerializerOptions);

        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        // Act
        var result = await _sut.ValidateApiKeyAsync(apiKey);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(result.CanExchangeTokens);
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_CacheException_Throws()
    {
        // Arrange
        var apiKey = "test-api-key";
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cache unavailable"));

        // Act & Assert
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _sut.ValidateApiKeyAsync(apiKey));
    }

    [TestMethod]
    public async Task ValidateApiKeyAsync_UsesSha256HashedKey()
    {
        // Arrange
        var apiKey = "my-secret-api-key";
        var expectedHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        var expectedCacheKey = $"token-service:api-keys:{expectedHash}";

        string? capturedCacheKey = null;
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => capturedCacheKey = key)
            .ReturnsAsync((byte[]?)null);

        // Act
        await _sut.ValidateApiKeyAsync(apiKey);

        // Assert
        Assert.AreEqual(expectedCacheKey, capturedCacheKey);
    }

    [TestMethod]
    public async Task RegisterServiceAsync_NullRegistration_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.RegisterServiceAsync(null!, "api-key"));
    }

    [TestMethod]
    public async Task RegisterServiceAsync_NullApiKey_ThrowsArgumentNullException()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash"
        };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.RegisterServiceAsync(registration, null!));
    }

    [TestMethod]
    public async Task RegisterServiceAsync_EmptyApiKey_ThrowsArgumentException()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash"
        };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _sut.RegisterServiceAsync(registration, string.Empty));
    }

    [TestMethod]
    public async Task RegisterServiceAsync_ValidRegistration_StoresBothKeys()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash"
        };
        var apiKey = "new-api-key";

        var storedKeys = new List<string>();
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cacheMock.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, _, _, _) => storedKeys.Add(key))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RegisterServiceAsync(registration, apiKey);

        // Assert
        Assert.AreEqual(2, storedKeys.Count);
        Assert.IsTrue(storedKeys.Exists(k => k.StartsWith("token-service:api-keys:")));
        Assert.IsTrue(storedKeys.Exists(k => k.StartsWith("token-service:service-index:")));
    }

    [TestMethod]
    public async Task RegisterServiceAsync_WithExistingKey_RemovesOldKey()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash"
        };
        var newApiKey = "new-api-key";
        var oldApiKeyHash = "old-hash-value";

        var removedKeys = new List<string>();
        _cacheMock.Setup(c => c.GetAsync(
                It.Is<string>(s => s.Contains("service-index")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(oldApiKeyHash));
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => removedKeys.Add(key))
            .Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RegisterServiceAsync(registration, newApiKey);

        // Assert
        Assert.AreEqual(1, removedKeys.Count);
        Assert.IsTrue(removedKeys[0].Contains(oldApiKeyHash));
    }

    [TestMethod]
    public async Task RegisterServiceAsync_SameKeyHash_DoesNotRemoveOldKey()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash"
        };
        var apiKey = "my-api-key";
        var apiKeyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

        var removedKeys = new List<string>();
        _cacheMock.Setup(c => c.GetAsync(
                It.Is<string>(s => s.Contains("service-index")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(apiKeyHash));
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => removedKeys.Add(key))
            .Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RegisterServiceAsync(registration, apiKey);

        // Assert
        Assert.AreEqual(0, removedKeys.Count);
    }

    [TestMethod]
    public async Task RegisterServiceAsync_SetsCorrectCacheExpiration()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash"
        };
        var apiKey = "test-api-key";

        DistributedCacheEntryOptions? capturedOptions = null;
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cacheMock.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, _, options, _) => capturedOptions = options)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RegisterServiceAsync(registration, apiKey);

        // Assert
        Assert.IsNotNull(capturedOptions);
        Assert.AreEqual(_settings.ApiKeyCacheDuration, capturedOptions.AbsoluteExpirationRelativeToNow);
    }

    [TestMethod]
    public async Task RegisterServiceAsync_ServiceNameIndex_UsesUpperCase()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "Test-Service",
            ApiKeyHash = "hash"
        };
        var apiKey = "test-api-key";

        var storedKeys = new List<string>();
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cacheMock.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, _, _, _) => storedKeys.Add(key))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RegisterServiceAsync(registration, apiKey);

        // Assert
        var indexKey = storedKeys.Find(k => k.Contains("service-index"));
        Assert.IsNotNull(indexKey);
        Assert.IsTrue(indexKey.Contains("TEST-SERVICE"));
    }

    [TestMethod]
    public async Task RemoveServiceAsync_NullServiceName_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.RemoveServiceAsync(null!));
    }

    [TestMethod]
    public async Task RemoveServiceAsync_EmptyServiceName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _sut.RemoveServiceAsync(string.Empty));
    }

    [TestMethod]
    public async Task RemoveServiceAsync_ServiceNotFound_ReturnsWithoutRemoving()
    {
        // Arrange
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var removeCount = 0;
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => removeCount++)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RemoveServiceAsync("non-existent-service");

        // Assert
        Assert.AreEqual(0, removeCount);
    }

    [TestMethod]
    public async Task RemoveServiceAsync_ServiceExists_RemovesBothKeys()
    {
        // Arrange
        var serviceName = "test-service";
        var apiKeyHash = "stored-hash";

        _cacheMock.Setup(c => c.GetAsync(
                It.Is<string>(s => s.Contains("service-index")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(apiKeyHash));

        var removedKeys = new List<string>();
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => removedKeys.Add(key))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RemoveServiceAsync(serviceName);

        // Assert
        Assert.AreEqual(2, removedKeys.Count);
        Assert.IsTrue(removedKeys.Exists(k => k.Contains("api-keys")));
        Assert.IsTrue(removedKeys.Exists(k => k.Contains("service-index")));
    }

    [TestMethod]
    public async Task RemoveServiceAsync_UsesUpperCaseServiceName()
    {
        // Arrange
        var serviceName = "Test-Service";

        string? capturedIndexKey = null;
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => capturedIndexKey = key)
            .ReturnsAsync((byte[]?)null);

        // Act
        await _sut.RemoveServiceAsync(serviceName);

        // Assert
        Assert.IsNotNull(capturedIndexKey);
        Assert.IsTrue(capturedIndexKey.Contains("TEST-SERVICE"));
    }

    [TestMethod]
    public async Task RegisterServiceAsync_UpdatesApiKeyHash_InStoredRegistration()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "original-hash"
        };
        var apiKey = "my-api-key";
        var expectedHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

        byte[]? storedData = null;
        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cacheMock.Setup(c => c.SetAsync(
                It.Is<string>(s => s.Contains("api-keys")),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, data, _, _) => storedData = data)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RegisterServiceAsync(registration, apiKey);

        // Assert
        Assert.IsNotNull(storedData);
        var storedRegistration = JsonSerializer.Deserialize<ServiceRegistration>(
            Encoding.UTF8.GetString(storedData), Constants.JsonSerializerOptions);
        Assert.IsNotNull(storedRegistration);
        Assert.AreEqual(expectedHash, storedRegistration.ApiKeyHash);
    }
}
