using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class TokenModelTests
{
    [TestMethod]
    public void TokenResponse_UnspecifiedExpiry_TreatsValueAsUtc()
    {
        var expiry = new DateTime(2026, 8, 18, 12, 30, 0, DateTimeKind.Unspecified);
        var response = new TokenResponse
        {
            AccessToken = "token",
            TokenType = "Bearer",
            ExpiresIn = 60,
            ExpiresAt = expiry,
            Subject = "service-a"
        };

        Assert.AreEqual(new DateTimeOffset(expiry, TimeSpan.Zero), response.ExpiresAtUtc);
    }

    [TestMethod]
    public void TokenValidationResult_LocalExpiry_ConvertsValueToUtc()
    {
        var expiry = new DateTime(2026, 8, 18, 12, 30, 0, DateTimeKind.Local);
        var result = new TokenValidationResult
        {
            IsValid = true,
            ExpiresAt = expiry
        };

        Assert.AreEqual(new DateTimeOffset(expiry).ToUniversalTime(), result.ExpiresAtUtc);
    }
}
