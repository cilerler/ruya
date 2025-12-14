using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Ruya.Services.TokenBroker;

public sealed class TokenBrokerHealthCheck : IHealthCheck
{
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<TokenBrokerHealthCheck> _logger;

    public TokenBrokerHealthCheck(
        IDistributedCache distributedCache,
        ILogger<TokenBrokerHealthCheck> logger)
    {
        _distributedCache = distributedCache;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Test cache connectivity
            var testValue = DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);

            await _distributedCache.SetStringAsync(
                Constants.CacheKeys.HealthCheck,
                testValue,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = Constants.Defaults.HealthCheckKeyExpiry
                },
                cancellationToken);

            var retrieved = await _distributedCache.GetStringAsync(Constants.CacheKeys.HealthCheck, cancellationToken);

            if (retrieved == testValue)
            {
                return HealthCheckResult.Healthy("Token Service is operational");
            }

            _logger.LogWarning("Cache read/write mismatch in health check");
            return HealthCheckResult.Degraded("Cache read/write mismatch");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token Service health check failed");
            return HealthCheckResult.Unhealthy("Cache connectivity failed", ex);
        }
        finally
        {
            // Always attempt to clean up the test key
            try
            {
                await _distributedCache.RemoveAsync(Constants.CacheKeys.HealthCheck, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to clean up health check key");
            }
        }
    }
}
