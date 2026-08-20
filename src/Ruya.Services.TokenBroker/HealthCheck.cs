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
    private readonly TimeProvider _timeProvider;

    [Obsolete("Use the constructor that accepts TimeProvider.")]
    public TokenBrokerHealthCheck(
        IDistributedCache distributedCache,
        ILogger<TokenBrokerHealthCheck> logger)
        : this(distributedCache, logger, TimeProvider.System)
    {
    }

    public TokenBrokerHealthCheck(
        IDistributedCache distributedCache,
        ILogger<TokenBrokerHealthCheck> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(distributedCache);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _distributedCache = distributedCache;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var healthCheckKey = $"{Constants.CacheKeys.HealthCheckPrefix}{Guid.NewGuid():N}";
        try
        {
            var testValue = _timeProvider.GetUtcNow().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);

            await _distributedCache.SetStringAsync(
                healthCheckKey,
                testValue,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = Constants.Defaults.HealthCheckKeyExpiry
                },
                cancellationToken);

            var retrieved = await _distributedCache.GetStringAsync(healthCheckKey, cancellationToken);

            if (retrieved == testValue)
            {
                return HealthCheckResult.Healthy("Token Service is operational");
            }

            _logger.HealthCheckCacheMismatch();
            return HealthCheckResult.Degraded("Cache read/write mismatch");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.HealthCheckFailed(ex.GetType().Name);
            return HealthCheckResult.Unhealthy("Cache connectivity failed", ex);
        }
        finally
        {
            // Always attempt to clean up the test key
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _distributedCache.RemoveAsync(healthCheckKey, cleanupTimeout.Token);
            }
            catch (Exception ex)
            {
                _logger.HealthCheckCleanupFailed(ex.GetType().Name);
            }
        }
    }
}
