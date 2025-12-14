using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Exceptions;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class TokenBrokerTests
{
    private Mock<ILogger<TokenBroker>> _loggerMock = null!;
    private Mock<IMeterFactory> _meterFactoryMock = null!;
    private Mock<IOptions<TokenBrokerSettings>> _optionsMock = null!;
    private TokenBrokerSettings _settings = null!;
    private TokenBroker _sut = null!;
    private FakeTimeProvider _timeProvider = null!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<TokenBroker>>();
        _meterFactoryMock = new Mock<IMeterFactory>();
        _optionsMock = new Mock<IOptions<TokenBrokerSettings>>();
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        _settings = new TokenBrokerSettings
        {
            Issuer = "test-issuer",
            Audiences = ["test-audience"],
            SigningKeyBase64 = Convert.ToBase64String(new byte[32]),
            TokenLifetime = TimeSpan.FromMinutes(15),
            ClockSkew = TimeSpan.FromSeconds(30),
            ApiKeyCacheDuration = TimeSpan.FromMinutes(5)
        };

        _optionsMock.Setup(o => o.Value).Returns(_settings);

        var meter = new Meter("Ruya.TokenBroker", "1.0.0");
        _meterFactoryMock.Setup(m => m.Create(It.IsAny<MeterOptions>()))
            .Returns(meter);

        _sut = new TokenBroker(
            _loggerMock.Object,
            _meterFactoryMock.Object,
            _optionsMock.Object,
            _timeProvider);
    }

    [TestMethod]
    public async Task CreateTokenAsync_ValidRequest_ReturnsToken()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-service",
            Scopes = ["scope1", "scope2"]
        };

        // Act
        var result = await _sut.CreateTokenAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.AccessToken);
        Assert.AreEqual("Bearer", result.TokenType);
        Assert.AreEqual(request.Subject, result.Subject);
        Assert.AreEqual(2, result.Scopes.Count);
    }

    [TestMethod]
    public async Task CreateTokenAsync_WithCustomLifetime_RespectsLifetime()
    {
        // Arrange
        var customLifetime = TimeSpan.FromHours(1);
        var request = new TokenRequest
        {
            Subject = "test-service",
            CustomLifetime = customLifetime
        };

        // Act
        var result = await _sut.CreateTokenAsync(request);

        // Assert
        Assert.IsNotNull(result);
        // Allow small delta for execution time
        var expectedExpiry = DateTime.UtcNow.Add(customLifetime);
        var diff = (result.ExpiresAt - expectedExpiry).Duration();
        Assert.IsTrue(diff < TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ValidateTokenAsync_ValidToken_ReturnsValidResult()
    {
        // Arrange
        var request = new TokenRequest { Subject = "test-service" };
        var tokenResponse = await _sut.CreateTokenAsync(request);

        // Act
        var result = await _sut.ValidateTokenAsync(tokenResponse.AccessToken);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(request.Subject, result.Subject);
    }

    [TestMethod]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsInvalidResult()
    {
        // Arrange
        var invalidToken = "invalid-token-string";

        // Act
        var result = await _sut.ValidateTokenAsync(invalidToken);

        // Assert
        Assert.IsFalse(result.IsValid);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task CreateTokenAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.CreateTokenAsync(null!));
    }

    [TestMethod]
    public async Task CreateTokenAsync_NullSubject_ThrowsArgumentNullException()
    {
        // Arrange
        var request = new TokenRequest { Subject = null! };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.CreateTokenAsync(request));
    }

    [TestMethod]
    public async Task CreateTokenAsync_EmptySubject_ThrowsArgumentException()
    {
        // Arrange
        var request = new TokenRequest { Subject = string.Empty };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _sut.CreateTokenAsync(request));
    }

    [TestMethod]
    public async Task CreateTokenAsync_WhitespaceSubject_ThrowsArgumentException()
    {
        // Arrange
        var request = new TokenRequest { Subject = "   " };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _sut.CreateTokenAsync(request));
    }

    [TestMethod]
    public async Task CreateTokenAsync_WithName_IncludesNameClaim()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-service",
            Name = "Test Service Display Name"
        };

        // Act
        var result = await _sut.CreateTokenAsync(request);
        var validationResult = await _sut.ValidateTokenAsync(result.AccessToken);

        // Assert
        Assert.IsTrue(validationResult.IsValid);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);
        var nameClaim = token.Claims.FirstOrDefault(c => c.Type == "name");
        Assert.IsNotNull(nameClaim);
        Assert.AreEqual(request.Name, nameClaim.Value);
    }

    [TestMethod]
    public async Task CreateTokenAsync_WithRoles_IncludesRoleClaims()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-service",
            Roles = ["admin", "user", "reader"]
        };

        // Act
        var result = await _sut.CreateTokenAsync(request);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        // Assert
        var roleClaims = token.Claims.Where(c => c.Type == ClaimTypes.Role).ToList();
        Assert.AreEqual(3, roleClaims.Count);
        Assert.IsTrue(roleClaims.Any(c => c.Value == "admin"));
        Assert.IsTrue(roleClaims.Any(c => c.Value == "user"));
        Assert.IsTrue(roleClaims.Any(c => c.Value == "reader"));
    }

    [TestMethod]
    public async Task CreateTokenAsync_WithAdditionalClaims_IncludesCustomClaims()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-service",
            AdditionalClaims = new Dictionary<string, string>
            {
                ["custom_claim_1"] = "value1",
                ["custom_claim_2"] = "value2"
            }
        };

        // Act
        var result = await _sut.CreateTokenAsync(request);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        // Assert
        var custom1 = token.Claims.FirstOrDefault(c => c.Type == "custom_claim_1");
        var custom2 = token.Claims.FirstOrDefault(c => c.Type == "custom_claim_2");
        Assert.IsNotNull(custom1);
        Assert.IsNotNull(custom2);
        Assert.AreEqual("value1", custom1.Value);
        Assert.AreEqual("value2", custom2.Value);
    }

    [TestMethod]
    public async Task CreateTokenAsync_WithMultipleAudiences_IncludesAllAudiences()
    {
        // Arrange - Create new service with multiple audiences
        var multiAudienceSettings = new TokenBrokerSettings
        {
            Issuer = "test-issuer",
            Audiences = ["audience1", "audience2", "audience3"],
            SigningKeyBase64 = Convert.ToBase64String(new byte[32]),
            TokenLifetime = TimeSpan.FromMinutes(15),
            ClockSkew = TimeSpan.FromSeconds(30),
            ApiKeyCacheDuration = TimeSpan.FromMinutes(5)
        };
        var multiAudienceOptionsMock = new Mock<IOptions<TokenBrokerSettings>>();
        multiAudienceOptionsMock.Setup(o => o.Value).Returns(multiAudienceSettings);

        var sut = new TokenBroker(
            _loggerMock.Object,
            _meterFactoryMock.Object,
            multiAudienceOptionsMock.Object,
            _timeProvider);

        var request = new TokenRequest { Subject = "test-service" };

        // Act
        var result = await sut.CreateTokenAsync(request);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        // Assert
        var audiences = token.Audiences.ToList();
        Assert.AreEqual(3, audiences.Count);
        Assert.IsTrue(audiences.Contains("audience1"));
        Assert.IsTrue(audiences.Contains("audience2"));
        Assert.IsTrue(audiences.Contains("audience3"));
    }

    [TestMethod]
    public async Task CreateTokenAsync_WithNoScopes_ReturnsNullScopes()
    {
        // Arrange
        var request = new TokenRequest { Subject = "test-service" };

        // Act
        var result = await _sut.CreateTokenAsync(request);

        // Assert
        Assert.IsNull(result.Scopes);
    }

    [TestMethod]
    public async Task CreateTokenAsync_WithEmptyScopes_ReturnsEmptyScopes()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-service",
            Scopes = []
        };

        // Act
        var result = await _sut.CreateTokenAsync(request);

        // Assert
        Assert.IsNotNull(result.Scopes);
        Assert.AreEqual(0, result.Scopes.Count);
    }

    [TestMethod]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsInvalidResult()
    {
        // Arrange
        // Create a token service with time in the past
        var pastTime = DateTimeOffset.UtcNow.AddHours(-2);
        var pastTimeProvider = new FakeTimeProvider(pastTime);

        var pastSut = new TokenBroker(
            _loggerMock.Object,
            _meterFactoryMock.Object,
            _optionsMock.Object,
            pastTimeProvider);

        var request = new TokenRequest
        {
            Subject = "test-service",
            CustomLifetime = TimeSpan.FromMinutes(1)
        };

        // Create token using past time (will be already expired)
        var tokenResponse = await pastSut.CreateTokenAsync(request);

        // Act - validate with current time service (token should be expired)
        var validationResult = await _sut.ValidateTokenAsync(tokenResponse.AccessToken);

        // Assert
        Assert.IsFalse(validationResult.IsValid);
        Assert.IsNotNull(validationResult.ErrorMessage);
    }

    [TestMethod]
    public async Task ValidateTokenAsync_ValidToken_ExtractsRoles()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-service",
            Roles = ["admin", "user"]
        };
        var tokenResponse = await _sut.CreateTokenAsync(request);

        // Act
        var result = await _sut.ValidateTokenAsync(tokenResponse.AccessToken);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.IsNotNull(result.Roles);
        Assert.AreEqual(2, result.Roles.Count);
        Assert.IsTrue(result.Roles.Contains("admin"));
        Assert.IsTrue(result.Roles.Contains("user"));
    }

    [TestMethod]
    public async Task ValidateTokenAsync_ValidToken_ExtractsScopes()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-service",
            Scopes = ["read", "write"]
        };
        var tokenResponse = await _sut.CreateTokenAsync(request);

        // Act
        var result = await _sut.ValidateTokenAsync(tokenResponse.AccessToken);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.IsNotNull(result.Scopes);
        Assert.AreEqual(2, result.Scopes.Count);
        Assert.IsTrue(result.Scopes.Contains("read"));
        Assert.IsTrue(result.Scopes.Contains("write"));
    }

    [TestMethod]
    public async Task ValidateTokenAsync_NullToken_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.ValidateTokenAsync(null!));
    }

    [TestMethod]
    public async Task ValidateTokenAsync_EmptyToken_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _sut.ValidateTokenAsync(string.Empty));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_ValidToken_ReturnsExchangedToken()
    {
        // Arrange
        var originalRequest = new TokenRequest
        {
            Subject = "original-user",
            Scopes = ["read", "write"]
        };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        var exchangeRequest = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = "service-a"
        };

        // Act
        var result = await _sut.ExchangeTokenAsync(exchangeRequest);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.AccessToken);
        Assert.AreEqual("original-user", result.Subject);
        Assert.IsNotNull(result.Actor);
        Assert.AreEqual("service-a", result.Actor.Subject);
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.ExchangeTokenAsync(null!));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_NullOriginalToken_ThrowsArgumentNullException()
    {
        // Arrange
        var request = new TokenExchangeRequest
        {
            OriginalToken = null!,
            ActorService = "service-a"
        };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.ExchangeTokenAsync(request));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_EmptyActorService_ThrowsArgumentException()
    {
        // Arrange
        var originalRequest = new TokenRequest { Subject = "original-user" };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        var request = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = string.Empty
        };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _sut.ExchangeTokenAsync(request));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_InvalidOriginalToken_ThrowsSecurityTokenMalformedException()
    {
        // Arrange
        var request = new TokenExchangeRequest
        {
            OriginalToken = "invalid-token",
            ActorService = "service-a"
        };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<Microsoft.IdentityModel.Tokens.SecurityTokenMalformedException>(
            () => _sut.ExchangeTokenAsync(request));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_WithNarrowedScopes_ReturnsNarrowedScopes()
    {
        // Arrange
        var originalRequest = new TokenRequest
        {
            Subject = "original-user",
            Scopes = ["read", "write", "delete"]
        };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        var exchangeRequest = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = "service-a",
            NarrowedScopes = ["read"]
        };

        // Act
        var result = await _sut.ExchangeTokenAsync(exchangeRequest);

        // Assert
        Assert.IsNotNull(result.Scopes);
        Assert.AreEqual(1, result.Scopes.Count);
        Assert.AreEqual("read", result.Scopes[0]);
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_WithElevatedScopes_ThrowsInvalidTokenException()
    {
        // Arrange
        var originalRequest = new TokenRequest
        {
            Subject = "original-user",
            Scopes = ["read"]
        };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        var exchangeRequest = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = "service-a",
            NarrowedScopes = ["read", "write"]
        };

        // Act & Assert
        var ex = await Assert.ThrowsExactlyAsync<InvalidTokenException>(
            () => _sut.ExchangeTokenAsync(exchangeRequest));
        Assert.IsTrue(ex.Message.Contains("write"));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_MultipleExchanges_BuildsNestedActorChain()
    {
        // Arrange
        var originalRequest = new TokenRequest
        {
            Subject = "original-user",
            Scopes = ["read"]
        };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        // First exchange
        var exchange1Request = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = "service-a"
        };
        var token1 = await _sut.ExchangeTokenAsync(exchange1Request);

        // Second exchange
        var exchange2Request = new TokenExchangeRequest
        {
            OriginalToken = token1.AccessToken,
            ActorService = "service-b"
        };
        var token2 = await _sut.ExchangeTokenAsync(exchange2Request);

        // Third exchange
        var exchange3Request = new TokenExchangeRequest
        {
            OriginalToken = token2.AccessToken,
            ActorService = "service-c"
        };
        var token3 = await _sut.ExchangeTokenAsync(exchange3Request);

        // Act
        var result = await _sut.ValidateTokenAsync(token3.AccessToken);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.IsNotNull(result.ActorChain);
        var chain = result.ActorChain.ToList();
        Assert.AreEqual(3, chain.Count);
        Assert.AreEqual("service-c", chain[0]);
        Assert.AreEqual("service-b", chain[1]);
        Assert.AreEqual("service-a", chain[2]);
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_WithCustomLifetime_RespectsLifetime()
    {
        // Arrange
        var originalRequest = new TokenRequest { Subject = "original-user" };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        var customLifetime = TimeSpan.FromHours(2);
        var exchangeRequest = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = "service-a",
            CustomLifetime = customLifetime
        };

        // Act
        var result = await _sut.ExchangeTokenAsync(exchangeRequest);

        // Assert
        var expectedExpiry = DateTime.UtcNow.Add(customLifetime);
        var diff = (result.ExpiresAt - expectedExpiry).Duration();
        Assert.IsTrue(diff < TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_PreservesOriginalSubject()
    {
        // Arrange
        var originalRequest = new TokenRequest { Subject = "original-user-123" };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        var exchangeRequest = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = "service-a"
        };

        // Act
        var result = await _sut.ExchangeTokenAsync(exchangeRequest);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        // Assert
        Assert.AreEqual("original-user-123", result.Subject);
        var originalSubClaim = token.Claims.FirstOrDefault(c => c.Type == "original_sub");
        Assert.IsNotNull(originalSubClaim);
        Assert.AreEqual("original-user-123", originalSubClaim.Value);
    }

    [TestMethod]
    public async Task ValidateTokenAsync_TokenWithActorChain_ParsesActorChain()
    {
        // Arrange
        var originalRequest = new TokenRequest { Subject = "original-user" };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        var exchangeRequest = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = "service-a"
        };
        var exchangedToken = await _sut.ExchangeTokenAsync(exchangeRequest);

        // Act
        var result = await _sut.ValidateTokenAsync(exchangedToken.AccessToken);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.IsNotNull(result.ActorChain);
        Assert.AreEqual("service-a", result.ActorChain.Subject);
        Assert.AreEqual("service-a", result.ImmediateActor);
    }

    [TestMethod]
    public async Task CreateTokenAsync_ResponseContainsCorrectExpiresIn()
    {
        // Arrange
        var customLifetime = TimeSpan.FromMinutes(30);
        var request = new TokenRequest
        {
            Subject = "test-service",
            CustomLifetime = customLifetime
        };

        // Act
        var result = await _sut.CreateTokenAsync(request);

        // Assert
        Assert.AreEqual((int)customLifetime.TotalSeconds, result.ExpiresIn);
    }

    [TestMethod]
    public async Task CreateTokenAsync_ResponseContainsCorrectTokenType()
    {
        // Arrange
        var request = new TokenRequest { Subject = "test-service" };

        // Act
        var result = await _sut.CreateTokenAsync(request);

        // Assert
        Assert.AreEqual("Bearer", result.TokenType);
    }

    [TestMethod]
    public async Task ExchangeTokenAsync_InheritsOriginalScopesWhenNoNarrowing()
    {
        // Arrange
        var originalRequest = new TokenRequest
        {
            Subject = "original-user",
            Scopes = ["read", "write", "admin"]
        };
        var originalToken = await _sut.CreateTokenAsync(originalRequest);

        var exchangeRequest = new TokenExchangeRequest
        {
            OriginalToken = originalToken.AccessToken,
            ActorService = "service-a"
        };

        // Act
        var result = await _sut.ExchangeTokenAsync(exchangeRequest);

        // Assert
        Assert.IsNotNull(result.Scopes);
        Assert.AreEqual(3, result.Scopes.Count);
        Assert.IsTrue(result.Scopes.Contains("read"));
        Assert.IsTrue(result.Scopes.Contains("write"));
        Assert.IsTrue(result.Scopes.Contains("admin"));
    }
}
