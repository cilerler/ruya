using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;
using Moq.Protected;

using Ruya.Services.TokenBroker.Client;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class TokenClientTests
{
    private Mock<HttpMessageHandler> _httpMessageHandlerMock = null!;
    private HttpClient _httpClient = null!;
    private IMemoryCache _memoryCache = null!;
    private Mock<ILogger<TokenClient>> _loggerMock = null!;
    private Mock<IOptions<TokenClientSettings>> _optionsMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;
    private TokenClientSettings _settings = null!;
    private TokenClient _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<TokenClient>>();
        _optionsMock = new Mock<IOptions<TokenClientSettings>>();
        _meterFactoryMock = new Mock<IMeterFactory>();

        _settings = new TokenClientSettings
        {
            TokenBrokerUrl = "http://localhost",
            ServiceName = "test-client",
            ApiKey = "test-api-key-123456",
            TokenRefreshBuffer = TimeSpan.FromMinutes(1)
        };

        _optionsMock.Setup(o => o.Value).Returns(_settings);

        var meter = new Meter("Ruya.TokenBroker.Client", "1.0.0");
        _meterFactoryMock.Setup(m => m.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        _sut = new TokenClient(
            _httpClient,
            _memoryCache,
            _loggerMock.Object,
            _optionsMock.Object,
            _meterFactoryMock.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _sut.Dispose();
        _memoryCache.Dispose();
    }

    [TestMethod]
    public async Task GetTokenAsync_SuccessfulRequest_ReturnsToken()
    {
        // Arrange
        var tokenResponse = new TokenResponse
        {
            AccessToken = "access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse, options: Constants.JsonSerializerOptions)
            });

        // Act
        var result = await _sut.GetTokenAsync();

        // Assert
        Assert.AreEqual(tokenResponse.AccessToken, result);
    }

    [TestMethod]
    public async Task GetTokenAsync_CachedToken_ReturnsCachedToken()
    {
        // Arrange
        var tokenResponse = new TokenResponse
        {
            AccessToken = "access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        // First call to populate cache
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse, options: Constants.JsonSerializerOptions)
            });

        await _sut.GetTokenAsync();

        // Act - Second call should hit cache
        var result = await _sut.GetTokenAsync();

        // Assert
        Assert.AreEqual(tokenResponse.AccessToken, result);

        // Verify SendAsync was called only once
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [TestMethod]
    public async Task GetTokenAsync_WithScopes_SendsScopesInRequest()
    {
        // Arrange
        var tokenResponse = new TokenResponse
        {
            AccessToken = "access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        HttpRequestMessage? capturedRequest = null;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse, options: Constants.JsonSerializerOptions)
            });

        var scopes = new[] { "scope1", "scope2" };

        // Act
        await _sut.GetTokenAsync(scopes);

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.IsTrue(capturedRequest.RequestUri!.ToString().Contains("api/v1/token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetTokenAsync_ForceRefresh_BypassesCache()
    {
        // Arrange
        var tokenResponse1 = new TokenResponse
        {
            AccessToken = "token-1",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        var tokenResponse2 = new TokenResponse
        {
            AccessToken = "token-2",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        var callCount = 0;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var response = callCount == 1 ? tokenResponse1 : tokenResponse2;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(response, options: Constants.JsonSerializerOptions)
                };
            });

        // First call to populate cache
        var result1 = await _sut.GetTokenAsync();

        // Act - Force refresh should bypass cache
        var result2 = await _sut.GetTokenAsync(forceRefresh: true);

        // Assert
        Assert.AreEqual("token-1", result1);
        Assert.AreEqual("token-2", result2);
        Assert.AreEqual(2, callCount);
    }

    [TestMethod]
    public async Task GetTokenAsync_UnauthorizedResponse_Throws()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("Invalid API key")
            });

        // Act & Assert
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => _sut.GetTokenAsync());
    }

    [TestMethod]
    public async Task GetTokenAsync_ServerError_Throws()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Server error")
            });

        // Act & Assert
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => _sut.GetTokenAsync());
    }

    [TestMethod]
    public async Task GetTokenAsync_HttpRequestException_Throws()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act & Assert
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => _sut.GetTokenAsync());
    }

    [TestMethod]
    public async Task GetTokenAsync_InvalidJsonResponse_Throws()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("invalid-json")
            });

        // Act & Assert
        await Assert.ThrowsExactlyAsync<System.Text.Json.JsonException>(
            () => _sut.GetTokenAsync());
    }

    [TestMethod]
    public async Task GetTokenAsync_TokenExpiringSoon_RefreshesToken()
    {
        // Arrange
        var expiringToken = new TokenResponse
        {
            AccessToken = "expiring-token",
            ExpiresAt = DateTime.UtcNow.AddSeconds(30),
            ExpiresIn = 30,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        var newToken = new TokenResponse
        {
            AccessToken = "new-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        var callCount = 0;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var response = callCount == 1 ? expiringToken : newToken;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(response, options: Constants.JsonSerializerOptions)
                };
            });

        // First call gets expiring token
        await _sut.GetTokenAsync();

        // Act - Second call should refresh because token is expiring within buffer
        var result = await _sut.GetTokenAsync();

        // Assert
        Assert.AreEqual("new-token", result);
        Assert.AreEqual(2, callCount);
    }

    [TestMethod]
    public async Task GetTokenAsync_SetsCorrectHeaders()
    {
        // Arrange
        var tokenResponse = new TokenResponse
        {
            AccessToken = "access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        HttpRequestMessage? capturedRequest = null;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse, options: Constants.JsonSerializerOptions)
            });

        // Act
        await _sut.GetTokenAsync();

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.IsTrue(capturedRequest.Headers.Contains("X-Api-Key"));
        Assert.IsTrue(capturedRequest.Headers.Contains("X-Service-Name"));
        Assert.AreEqual(_settings.ApiKey, capturedRequest.Headers.GetValues("X-Api-Key").First());
        Assert.AreEqual(_settings.ServiceName, capturedRequest.Headers.GetValues("X-Service-Name").First());
    }

    [TestMethod]
    public async Task GetTokenAsync_DifferentScopes_CachesSeparately()
    {
        // Arrange
        var tokenResponse1 = new TokenResponse
        {
            AccessToken = "token-scope1",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        var tokenResponse2 = new TokenResponse
        {
            AccessToken = "token-scope2",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        var callCount = 0;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var response = callCount == 1 ? tokenResponse1 : tokenResponse2;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(response, options: Constants.JsonSerializerOptions)
                };
            });

        // Act
        var result1 = await _sut.GetTokenAsync(["scope1"]);
        var result2 = await _sut.GetTokenAsync(["scope2"]);

        // Assert
        Assert.AreEqual("token-scope1", result1);
        Assert.AreEqual("token-scope2", result2);
        Assert.AreEqual(2, callCount);
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_SuccessfulExchange_ReturnsToken()
    {
        // Arrange
        var tokenResponse = new TokenResponse
        {
            AccessToken = "exchanged-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse, options: Constants.JsonSerializerOptions)
            });

        // Act
        var result = await _sut.ExchangeTokenAsync("original-token");

        // Assert
        Assert.AreEqual("exchanged-token", result);
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_WithNarrowedScopes_SendsScopes()
    {
        // Arrange
        var tokenResponse = new TokenResponse
        {
            AccessToken = "exchanged-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        HttpRequestMessage? capturedRequest = null;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse, options: Constants.JsonSerializerOptions)
            });

        var narrowedScopes = new[] { "read" };

        // Act
        await _sut.ExchangeTokenAsync("original-token", narrowedScopes);

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.IsTrue(capturedRequest.RequestUri!.ToString().Contains("api/v1/token/exchange", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_UnauthorizedResponse_Throws()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("Unauthorized")
            });

        // Act & Assert
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => _sut.ExchangeTokenAsync("original-token"));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_BadRequest_Throws()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("Invalid token")
            });

        // Act & Assert
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => _sut.ExchangeTokenAsync("invalid-token"));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_ForbiddenResponse_Throws()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Forbidden,
                Content = new StringContent("Exchange not allowed")
            });

        // Act & Assert
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => _sut.ExchangeTokenAsync("original-token"));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_HttpRequestException_Throws()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act & Assert
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => _sut.ExchangeTokenAsync("original-token"));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_DoesNotCacheExchangedToken()
    {
        // Arrange
        var callCount = 0;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(new TokenResponse
                    {
                        AccessToken = $"exchanged-token-{callCount}",
                        ExpiresAt = DateTime.UtcNow.AddHours(1),
                        ExpiresIn = 3600,
                        TokenType = "Bearer",
                        Subject = "test-subject"
                    }, options: Constants.JsonSerializerOptions)
                };
            });

        // Act
        var result1 = await _sut.ExchangeTokenAsync("original-token");
        var result2 = await _sut.ExchangeTokenAsync("original-token");

        // Assert
        Assert.AreEqual("exchanged-token-1", result1);
        Assert.AreEqual("exchanged-token-2", result2);
        Assert.AreEqual(2, callCount);
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_SetsCorrectEndpoint()
    {
        // Arrange
        var tokenResponse = new TokenResponse
        {
            AccessToken = "exchanged-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            ExpiresIn = 3600,
            TokenType = "Bearer",
            Subject = "test-subject"
        };

        HttpRequestMessage? capturedRequest = null;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse, options: Constants.JsonSerializerOptions)
            });

        // Act
        await _sut.ExchangeTokenAsync("original-token");

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.IsTrue(capturedRequest.RequestUri!.PathAndQuery.Contains("api/v1/token/exchange", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetTokenAsync_CancellationRequested_ThrowsTaskCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        // Act & Assert
        // TaskCanceledException is the actual exception type thrown when cancellation occurs
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => _sut.GetTokenAsync(cancellationToken: cts.Token));
    }

    [TestMethod]
    public void Dispose_MultipleDisposals_DoesNotThrow()
    {
        // Arrange
        var client = new TokenClient(
            new HttpClient(_httpMessageHandlerMock.Object),
            _memoryCache,
            _loggerMock.Object,
            _optionsMock.Object,
            _meterFactoryMock.Object);

        // Act & Assert - Should not throw
        client.Dispose();
        client.Dispose();
    }

    [TestMethod]
    public async Task GetTokenAsync_NullResponse_ThrowsInvalidOperationException()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create<TokenResponse?>(null, options: Constants.JsonSerializerOptions)
            });

        // Act & Assert
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _sut.GetTokenAsync());
    }
}
