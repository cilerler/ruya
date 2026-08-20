using System;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public sealed class TokenBrokerApiCompatibilityTests
{
    [TestMethod]
    public void PublicApi_ReleasedSecurityAndHealthMembers_RemainAsObsoleteBridges()
    {
        var apiKeyValidatorConstructor = typeof(ApiKeyValidator).GetConstructor(
        [
            typeof(ILogger<ApiKeyValidator>),
            typeof(IMeterFactory),
            typeof(IOptions<TokenBrokerSettings>),
            typeof(IDistributedCache)
        ]);
        var healthCheckConstructor = typeof(TokenBrokerHealthCheck).GetConstructor(
        [
            typeof(IDistributedCache),
            typeof(ILogger<TokenBrokerHealthCheck>)
        ]);
        // Intentional reflection names: verify released obsolete members without compile-time use or CS0618.
        var brokerSymmetricKey = typeof(TokenBrokerSettings).GetProperty("SigningKeyBase64");
        var validationSymmetricKey = typeof(TokenValidationSettings).GetProperty("SigningKeyBase64");
        var serializerOptions = typeof(Constants).GetField("JsonSerializerOptions");
        var healthCheckKey = typeof(Constants.CacheKeys).GetField("HealthCheck");

        AssertObsolete(apiKeyValidatorConstructor);
        AssertObsolete(healthCheckConstructor);
        AssertObsolete(brokerSymmetricKey);
        AssertObsolete(validationSymmetricKey);
        AssertObsolete(serializerOptions);
        AssertObsolete(healthCheckKey);

        Assert.AreEqual("token-service:health-check", healthCheckKey!.GetRawConstantValue());
    }

    private static void AssertObsolete(System.Reflection.MemberInfo? member)
    {
        Assert.IsNotNull(member);
        Assert.IsTrue(member.IsDefined(typeof(ObsoleteAttribute), inherit: false));
    }
}
