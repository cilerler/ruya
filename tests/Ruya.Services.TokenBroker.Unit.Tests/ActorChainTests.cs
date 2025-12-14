using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class ActorChainTests
{
    [TestMethod]
    public void ToList_SingleActor_ReturnsSingleElementList()
    {
        // Arrange
        var chain = new ActorChain { Subject = "service-a" };

        // Act
        var result = chain.ToList();

        // Assert
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("service-a", result[0]);
    }

    [TestMethod]
    public void ToList_NestedActors_ReturnsListFromOutermostToInnermost()
    {
        // Arrange
        var chain = new ActorChain
        {
            Subject = "service-c",
            Actor = new ActorChain
            {
                Subject = "service-b",
                Actor = new ActorChain { Subject = "service-a" }
            }
        };

        // Act
        var result = chain.ToList();

        // Assert
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("service-c", result[0]);
        Assert.AreEqual("service-b", result[1]);
        Assert.AreEqual("service-a", result[2]);
    }

    [TestMethod]
    public void ToList_DeeplyNestedActors_ReturnsCorrectOrder()
    {
        // Arrange
        var chain = new ActorChain
        {
            Subject = "5",
            Actor = new ActorChain
            {
                Subject = "4",
                Actor = new ActorChain
                {
                    Subject = "3",
                    Actor = new ActorChain
                    {
                        Subject = "2",
                        Actor = new ActorChain { Subject = "1" }
                    }
                }
            }
        };

        // Act
        var result = chain.ToList();

        // Assert
        Assert.AreEqual(5, result.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.AreEqual((5 - i).ToString(), result[i]);
        }
    }

    [TestMethod]
    public void FromList_SingleSubject_CreatesSingleActorChain()
    {
        // Arrange
        var subjects = new[] { "service-a" };

        // Act
        var result = ActorChain.FromList(subjects);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("service-a", result.Subject);
        Assert.IsNull(result.Actor);
    }

    [TestMethod]
    public void FromList_MultipleSubjects_CreatesNestedChain()
    {
        // Arrange
        var subjects = new[] { "service-c", "service-b", "service-a" };

        // Act
        var result = ActorChain.FromList(subjects);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("service-c", result.Subject);
        Assert.IsNotNull(result.Actor);
        Assert.AreEqual("service-b", result.Actor.Subject);
        Assert.IsNotNull(result.Actor.Actor);
        Assert.AreEqual("service-a", result.Actor.Actor.Subject);
        Assert.IsNull(result.Actor.Actor.Actor);
    }

    [TestMethod]
    public void FromList_EmptyList_ThrowsArgumentException()
    {
        // Arrange
        var subjects = Array.Empty<string>();

        // Act & Assert
        Assert.ThrowsExactly<ArgumentException>(
            () => ActorChain.FromList(subjects));
    }

    [TestMethod]
    public void FromList_ThenToList_RoundTrips()
    {
        // Arrange
        var original = new[] { "service-c", "service-b", "service-a" };

        // Act
        var chain = ActorChain.FromList(original);
        var result = chain.ToList();

        // Assert
        Assert.AreEqual(original.Length, result.Count);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.AreEqual(original[i], result[i]);
        }
    }

    [TestMethod]
    public void TryParse_ValidJson_ReturnsActorChain()
    {
        // Arrange
        var chain = new ActorChain
        {
            Subject = "service-a",
            Actor = new ActorChain { Subject = "service-b" }
        };
        var json = chain.ToJson();

        // Act
        var result = ActorChain.TryParse(json);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("service-a", result.Subject);
        Assert.IsNotNull(result.Actor);
        Assert.AreEqual("service-b", result.Actor.Subject);
    }

    [TestMethod]
    public void TryParse_NullJson_ReturnsNull()
    {
        // Act
        var result = ActorChain.TryParse(null);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryParse_EmptyJson_ReturnsNull()
    {
        // Act
        var result = ActorChain.TryParse(string.Empty);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryParse_WhitespaceJson_ReturnsNull()
    {
        // Act
        var result = ActorChain.TryParse("   ");

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryParse_LegacyFormat_ReturnsActorChain()
    {
        // Arrange - Legacy format used simple "sub" key
        var legacyJson = "{\"sub\":\"service-a\"}";

        // Act
        var result = ActorChain.TryParse(legacyJson);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("service-a", result.Subject);
        Assert.IsNull(result.Actor);
    }

    [TestMethod]
    public void TryParse_InvalidJson_ReturnsNull()
    {
        // Arrange
        var invalidJson = "not-valid-json";

        // Act
        var result = ActorChain.TryParse(invalidJson);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryParse_JsonWithMissingSubject_ReturnsNull()
    {
        // Arrange
        var json = "{\"actor\":{\"subject\":\"service-b\"}}";

        // Act
        var result = ActorChain.TryParse(json);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ToJson_SingleActor_ReturnsValidJson()
    {
        // Arrange
        var chain = new ActorChain { Subject = "service-a" };

        // Act
        var json = chain.ToJson();

        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(json));
        Assert.IsTrue(json.Contains("service-a"));
    }

    [TestMethod]
    public void ToJson_NestedActors_ReturnsValidJson()
    {
        // Arrange
        var chain = new ActorChain
        {
            Subject = "service-b",
            Actor = new ActorChain { Subject = "service-a" }
        };

        // Act
        var json = chain.ToJson();

        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(json));
        Assert.IsTrue(json.Contains("service-a"));
        Assert.IsTrue(json.Contains("service-b"));
    }

    [TestMethod]
    public void ToJson_ThenTryParse_RoundTrips()
    {
        // Arrange
        var original = new ActorChain
        {
            Subject = "service-c",
            Actor = new ActorChain
            {
                Subject = "service-b",
                Actor = new ActorChain { Subject = "service-a" }
            }
        };

        // Act
        var json = original.ToJson();
        var result = ActorChain.TryParse(json);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("service-c", result.Subject);
        Assert.IsNotNull(result.Actor);
        Assert.AreEqual("service-b", result.Actor.Subject);
        Assert.IsNotNull(result.Actor.Actor);
        Assert.AreEqual("service-a", result.Actor.Actor.Subject);
    }

    [TestMethod]
    public void ActorChain_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var chain1 = new ActorChain
        {
            Subject = "service-a",
            Actor = new ActorChain { Subject = "service-b" }
        };
        var chain2 = new ActorChain
        {
            Subject = "service-a",
            Actor = new ActorChain { Subject = "service-b" }
        };
        var chain3 = new ActorChain
        {
            Subject = "service-a",
            Actor = new ActorChain { Subject = "service-c" }
        };

        // Assert
        Assert.AreEqual(chain1, chain2);
        Assert.AreNotEqual(chain1, chain3);
    }

    [TestMethod]
    public void ActorChain_WithClause_CreatesNewInstance()
    {
        // Arrange
        var original = new ActorChain { Subject = "service-a" };

        // Act
        var modified = original with { Subject = "service-b" };

        // Assert
        Assert.AreEqual("service-a", original.Subject);
        Assert.AreEqual("service-b", modified.Subject);
    }
}

[TestClass]
public class TokenValidationResultTests
{
    [TestMethod]
    public void ImmediateActor_WithActorChain_ReturnsFirstActor()
    {
        // Arrange
        var result = new TokenValidationResult
        {
            IsValid = true,
            ActorChain = new ActorChain
            {
                Subject = "service-a",
                Actor = new ActorChain { Subject = "service-b" }
            }
        };

        // Act & Assert
        Assert.AreEqual("service-a", result.ImmediateActor);
    }

    [TestMethod]
    public void ImmediateActor_WithNoActorChain_ReturnsNull()
    {
        // Arrange
        var result = new TokenValidationResult
        {
            IsValid = true,
            ActorChain = null
        };

        // Act & Assert
        Assert.IsNull(result.ImmediateActor);
    }

    [TestMethod]
    public void ActorChainList_WithActorChain_ReturnsList()
    {
        // Arrange
        var result = new TokenValidationResult
        {
            IsValid = true,
            ActorChain = new ActorChain
            {
                Subject = "service-b",
                Actor = new ActorChain { Subject = "service-a" }
            }
        };

        // Act
        var list = result.ActorChainList;

        // Assert
        Assert.IsNotNull(list);
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("service-b", list[0]);
        Assert.AreEqual("service-a", list[1]);
    }

    [TestMethod]
    public void ActorChainList_WithNoActorChain_ReturnsNull()
    {
        // Arrange
        var result = new TokenValidationResult
        {
            IsValid = true,
            ActorChain = null
        };

        // Act & Assert
        Assert.IsNull(result.ActorChainList);
    }
}

[TestClass]
public class ServiceRegistrationTests
{
    [TestMethod]
    public void CanExchangeTokens_DefaultsToTrue()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash"
        };

        // Assert
        Assert.IsTrue(registration.CanExchangeTokens);
    }

    [TestMethod]
    public void ServiceRegistration_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var reg1 = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash",
            AllowedScopes = ["scope1"]
        };
        var reg2 = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash",
            AllowedScopes = ["scope1"]
        };

        // Assert
        Assert.AreEqual(reg1.ServiceName, reg2.ServiceName);
        Assert.AreEqual(reg1.ApiKeyHash, reg2.ApiKeyHash);
    }

    [TestMethod]
    public void ServiceRegistration_WithNullAllowedScopes_IsValid()
    {
        // Arrange
        var registration = new ServiceRegistration
        {
            ServiceName = "test-service",
            ApiKeyHash = "hash",
            AllowedScopes = null
        };

        // Assert
        Assert.IsNull(registration.AllowedScopes);
    }
}

[TestClass]
public class TokenRequestTests
{
    [TestMethod]
    public void TokenRequest_AllOptionalFieldsNull_IsValid()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-subject"
        };

        // Assert
        Assert.IsNotNull(request.Subject);
        Assert.IsNull(request.Name);
        Assert.IsNull(request.Roles);
        Assert.IsNull(request.Scopes);
        Assert.IsNull(request.CustomLifetime);
        Assert.IsNull(request.AdditionalClaims);
    }

    [TestMethod]
    public void TokenRequest_WithAllFields_IsValid()
    {
        // Arrange
        var request = new TokenRequest
        {
            Subject = "test-subject",
            Name = "Test Name",
            Roles = ["admin", "user"],
            Scopes = ["read", "write"],
            CustomLifetime = TimeSpan.FromHours(1),
            AdditionalClaims = new Dictionary<string, string>
            {
                ["custom"] = "value"
            }
        };

        // Assert
        Assert.AreEqual("test-subject", request.Subject);
        Assert.AreEqual("Test Name", request.Name);
        Assert.AreEqual(2, request.Roles!.Count);
        Assert.AreEqual(2, request.Scopes!.Count);
        Assert.AreEqual(TimeSpan.FromHours(1), request.CustomLifetime);
        Assert.AreEqual(1, request.AdditionalClaims!.Count);
    }
}

[TestClass]
public class TokenExchangeRequestTests
{
    [TestMethod]
    public void TokenExchangeRequest_MinimalFields_IsValid()
    {
        // Arrange
        var request = new TokenExchangeRequest
        {
            OriginalToken = "token",
            ActorService = "service-a"
        };

        // Assert
        Assert.AreEqual("token", request.OriginalToken);
        Assert.AreEqual("service-a", request.ActorService);
        Assert.IsNull(request.NarrowedScopes);
        Assert.IsNull(request.CustomLifetime);
    }

    [TestMethod]
    public void TokenExchangeRequest_WithAllFields_IsValid()
    {
        // Arrange
        var request = new TokenExchangeRequest
        {
            OriginalToken = "token",
            ActorService = "service-a",
            NarrowedScopes = ["read"],
            CustomLifetime = TimeSpan.FromMinutes(30)
        };

        // Assert
        Assert.AreEqual("token", request.OriginalToken);
        Assert.AreEqual("service-a", request.ActorService);
        Assert.AreEqual(1, request.NarrowedScopes!.Count);
        Assert.AreEqual(TimeSpan.FromMinutes(30), request.CustomLifetime);
    }
}

[TestClass]
public class TokenResponseTests
{
    [TestMethod]
    public void TokenResponse_RequiredFields_AreSet()
    {
        // Arrange
        var response = new TokenResponse
        {
            AccessToken = "token",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Subject = "test-subject"
        };

        // Assert
        Assert.AreEqual("token", response.AccessToken);
        Assert.AreEqual("Bearer", response.TokenType);
        Assert.AreEqual(3600, response.ExpiresIn);
        Assert.AreEqual("test-subject", response.Subject);
        Assert.IsNull(response.Actor);
        Assert.IsNull(response.Scopes);
    }

    [TestMethod]
    public void TokenResponse_WithActorAndScopes_IsValid()
    {
        // Arrange
        var response = new TokenResponse
        {
            AccessToken = "token",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Subject = "test-subject",
            Actor = new ActorChain { Subject = "service-a" },
            Scopes = ["read", "write"]
        };

        // Assert
        Assert.IsNotNull(response.Actor);
        Assert.AreEqual("service-a", response.Actor.Subject);
        Assert.AreEqual(2, response.Scopes!.Count);
    }
}
