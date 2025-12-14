using System;
using System.Collections.Generic;
using System.Security.Claims;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Ruya.Services.TokenBroker.Extensions;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class ClaimsPrincipalExtensionsTests
{
    [TestMethod]
    public void GetScopes_WithSingleScopeClaim_ReturnsScopes()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "read write delete")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var scopes = principal.GetScopes();

        // Assert
        Assert.AreEqual(3, scopes.Count);
        Assert.IsTrue(scopes.Contains("read"));
        Assert.IsTrue(scopes.Contains("write"));
        Assert.IsTrue(scopes.Contains("delete"));
    }

    [TestMethod]
    public void GetScopes_WithMultipleScopeClaims_ReturnsAllScopes()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "read write"),
            new(Constants.ScopeClaimType, "delete admin")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var scopes = principal.GetScopes();

        // Assert
        Assert.AreEqual(4, scopes.Count);
        Assert.IsTrue(scopes.Contains("read"));
        Assert.IsTrue(scopes.Contains("write"));
        Assert.IsTrue(scopes.Contains("delete"));
        Assert.IsTrue(scopes.Contains("admin"));
    }

    [TestMethod]
    public void GetScopes_WithNoScopeClaims_ReturnsEmptySet()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "test-subject")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var scopes = principal.GetScopes();

        // Assert
        Assert.AreEqual(0, scopes.Count);
    }

    [TestMethod]
    public void GetScopes_NullPrincipal_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(
            () => ClaimsPrincipalExtensions.GetScopes(null!));
    }

    [TestMethod]
    public void GetScopes_CaseInsensitiveComparison()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "Read WRITE Delete")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var scopes = principal.GetScopes();

        // Assert
        Assert.IsTrue(scopes.Contains("read"));
        Assert.IsTrue(scopes.Contains("READ"));
        Assert.IsTrue(scopes.Contains("Write"));
    }

    [TestMethod]
    public void HasAllScopes_WithAllRequiredScopes_ReturnsTrue()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "read write delete admin")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasAllScopes("read", "write");

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasAllScopes_MissingSomeScopes_ReturnsFalse()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "read")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasAllScopes("read", "write");

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasAllScopes_EmptyRequiredScopes_ReturnsTrue()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "read")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasAllScopes();

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasAllScopes_CaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "READ WRITE")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasAllScopes("read", "write");

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasAnyScope_WithMatchingScope_ReturnsTrue()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "read")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasAnyScope("read", "write", "admin");

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasAnyScope_WithNoMatchingScope_ReturnsFalse()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "read")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasAnyScope("write", "admin");

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasAnyScope_EmptyRequestedScopes_ReturnsFalse()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(Constants.ScopeClaimType, "read")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasAnyScope();

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void GetSubject_WithSubClaim_ReturnsSubject()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "test-subject-123")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetSubject();

        // Assert
        Assert.AreEqual("test-subject-123", result);
    }

    [TestMethod]
    public void GetSubject_WithNameIdentifierClaim_ReturnsSubject()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-subject-456")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetSubject();

        // Assert
        Assert.AreEqual("test-subject-456", result);
    }

    [TestMethod]
    public void GetSubject_WithBothClaims_PrefersSubClaim()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "sub-value"),
            new(ClaimTypes.NameIdentifier, "name-id-value")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetSubject();

        // Assert
        Assert.AreEqual("sub-value", result);
    }

    [TestMethod]
    public void GetSubject_WithNoSubjectClaim_ReturnsNull()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("name", "test-name")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetSubject();

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetSubject_NullPrincipal_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(
            () => ClaimsPrincipalExtensions.GetSubject(null!));
    }

    [TestMethod]
    public void GetActorChain_WithValidActorClaim_ReturnsActorChain()
    {
        // Arrange
        var actorChain = new ActorChain
        {
            Subject = "service-a",
            Actor = new ActorChain { Subject = "service-b" }
        };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetActorChain();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("service-a", result.Subject);
        Assert.IsNotNull(result.Actor);
        Assert.AreEqual("service-b", result.Actor.Subject);
    }

    [TestMethod]
    public void GetActorChain_WithNoActorClaim_ReturnsNull()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "test-subject")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetActorChain();

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetActorChain_NullPrincipal_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(
            () => ClaimsPrincipalExtensions.GetActorChain(null!));
    }

    [TestMethod]
    public void GetImmediateActor_WithActorChain_ReturnsImmediateActor()
    {
        // Arrange
        var actorChain = new ActorChain
        {
            Subject = "service-a",
            Actor = new ActorChain { Subject = "service-b" }
        };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetImmediateActor();

        // Assert
        Assert.AreEqual("service-a", result);
    }

    [TestMethod]
    public void GetImmediateActor_WithNoActorChain_ReturnsNull()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "test-subject")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetImmediateActor();

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetActorChainList_WithActorChain_ReturnsListFromOutermostToInnermost()
    {
        // Arrange
        var actorChain = new ActorChain
        {
            Subject = "service-c",
            Actor = new ActorChain
            {
                Subject = "service-b",
                Actor = new ActorChain { Subject = "service-a" }
            }
        };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetActorChainList();

        // Assert
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("service-c", result[0]);
        Assert.AreEqual("service-b", result[1]);
        Assert.AreEqual("service-a", result[2]);
    }

    [TestMethod]
    public void GetActorChainList_WithNoActorChain_ReturnsEmptyList()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "test-subject")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetActorChainList();

        // Assert
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetOriginalActor_WithActorChain_ReturnsLastActor()
    {
        // Arrange
        var actorChain = new ActorChain
        {
            Subject = "service-c",
            Actor = new ActorChain
            {
                Subject = "service-b",
                Actor = new ActorChain { Subject = "service-a" }
            }
        };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetOriginalActor();

        // Assert
        Assert.AreEqual("service-a", result);
    }

    [TestMethod]
    public void GetOriginalActor_WithSingleActor_ReturnsThatActor()
    {
        // Arrange
        var actorChain = new ActorChain { Subject = "service-a" };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetOriginalActor();

        // Assert
        Assert.AreEqual("service-a", result);
    }

    [TestMethod]
    public void GetOriginalActor_WithNoActorChain_ReturnsNull()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "test-subject")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetOriginalActor();

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void IsOnBehalfOf_WithActorClaim_ReturnsTrue()
    {
        // Arrange
        var actorChain = new ActorChain { Subject = "service-a" };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.IsOnBehalfOf();

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsOnBehalfOf_WithNoActorClaim_ReturnsFalse()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "test-subject")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.IsOnBehalfOf();

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsOnBehalfOf_NullPrincipal_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(
            () => ClaimsPrincipalExtensions.IsOnBehalfOf(null!));
    }

    [TestMethod]
    public void GetOriginalSubject_WithOriginalSubClaim_ReturnsOriginalSubject()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "acting-service"),
            new(Constants.OriginalSubjectClaimType, "original-user")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetOriginalSubject();

        // Assert
        Assert.AreEqual("original-user", result);
    }

    [TestMethod]
    public void GetOriginalSubject_WithNoOriginalSubClaim_FallsBackToSubject()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "regular-subject")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.GetOriginalSubject();

        // Assert
        Assert.AreEqual("regular-subject", result);
    }

    [TestMethod]
    public void GetOriginalSubject_NullPrincipal_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(
            () => ClaimsPrincipalExtensions.GetOriginalSubject(null!));
    }

    [TestMethod]
    public void HasActorInChain_WithActorPresent_ReturnsTrue()
    {
        // Arrange
        var actorChain = new ActorChain
        {
            Subject = "service-c",
            Actor = new ActorChain
            {
                Subject = "service-b",
                Actor = new ActorChain { Subject = "service-a" }
            }
        };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasActorInChain("service-b");

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasActorInChain_WithActorNotPresent_ReturnsFalse()
    {
        // Arrange
        var actorChain = new ActorChain
        {
            Subject = "service-a",
            Actor = new ActorChain { Subject = "service-b" }
        };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasActorInChain("service-c");

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasActorInChain_CaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var actorChain = new ActorChain { Subject = "Service-A" };
        var claims = new List<Claim>
        {
            new(Constants.ActorClaimType, actorChain.ToJson())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasActorInChain("service-a");

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasActorInChain_WithNoActorChain_ReturnsFalse()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("sub", "test-subject")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Act
        var result = principal.HasActorInChain("service-a");

        // Assert
        Assert.IsFalse(result);
    }
}
