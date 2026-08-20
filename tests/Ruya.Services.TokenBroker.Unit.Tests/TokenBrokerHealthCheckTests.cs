using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class TokenBrokerHealthCheckTests
{
    [TestMethod]
    public async Task CheckHealthAsync_ConcurrentSafeChecks_UseUniqueEphemeralKeys()
    {
        var values = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var writtenKeys = new List<string>();
        var cache = new Mock<IDistributedCache>();
        cache.Setup(instance => instance.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((key, value, _, _) =>
            {
                values[key] = value;
                writtenKeys.Add(key);
            })
            .Returns(Task.CompletedTask);
        cache.Setup(instance => instance.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((key, _) =>
                Task.FromResult(values.TryGetValue(key, out var value) ? value : null));
        cache.Setup(instance => instance.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => values.Remove(key))
            .Returns(Task.CompletedTask);
        var healthCheck = new TokenBrokerHealthCheck(
            cache.Object,
            Mock.Of<ILogger<TokenBrokerHealthCheck>>(),
            TimeProvider.System);

        var first = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        var second = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Healthy, first.Status);
        Assert.AreEqual(HealthStatus.Healthy, second.Status);
        Assert.AreEqual(2, writtenKeys.Count);
        Assert.AreNotEqual(writtenKeys[0], writtenKeys[1]);
        Assert.IsEmpty(values);
    }

    [TestMethod]
    public async Task CheckHealthAsync_CanceledRequest_PropagatesCancellation()
    {
        var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
        var healthCheck = new TokenBrokerHealthCheck(
            cache.Object,
            Mock.Of<ILogger<TokenBrokerHealthCheck>>(),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellation.Token));
        cache.VerifyNoOtherCalls();
    }
}
