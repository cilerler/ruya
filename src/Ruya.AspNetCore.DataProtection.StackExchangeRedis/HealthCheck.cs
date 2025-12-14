using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

using Ruya.AspNetCore.DataProtection.StackExchangeRedis.Contracts;

using StackExchange.Redis;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

/// <summary>
/// Health check for data protection service and Redis connectivity.
/// </summary>
public sealed class DataProtectionHealthCheck : IHealthCheck
{
    private const string TestContent = "health-check-test";

    private readonly ILogger<DataProtectionHealthCheck> _logger;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProtectionHealthCheck"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    public DataProtectionHealthCheck(
        ILogger<DataProtectionHealthCheck> logger,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if using lazy initialization (client mode)
            var lazySettings = _serviceProvider.GetService<AsyncLazy<DataProtectionSettings>>();
            var lazyRedis = _serviceProvider.GetService<AsyncLazy<IConnectionMultiplexer>>();

            if (lazySettings is not null)
            {
                // Client mode with lazy initialization
                if (!lazySettings.IsValueCreated)
                {
                    return HealthCheckResult.Unhealthy("Data protection settings are not yet initialized.");
                }

                if (lazyRedis is not null && !lazyRedis.IsValueCreated)
                {
                    return HealthCheckResult.Unhealthy("Redis connection is not yet initialized.");
                }
            }

            // Get the actual services (will be immediately available if already initialized)
            var connectionMultiplexer = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            var dataProtection = _serviceProvider.GetRequiredService<IDataProtection>();

            // Check Redis connectivity
            if (!connectionMultiplexer.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Redis connection is not established.");
            }

            var database = connectionMultiplexer.GetDatabase();
            var pingResult = await database.PingAsync().ConfigureAwait(false);

            if (pingResult > TimeSpan.FromSeconds(5))
            {
                return HealthCheckResult.Degraded($"Redis ping latency is high: {pingResult.TotalMilliseconds}ms");
            }

            // Check data protection roundtrip
            var protectedContent = dataProtection.Protect(TestContent);
            var unprotectedContent = dataProtection.Unprotect(protectedContent);

            if (!string.Equals(TestContent, unprotectedContent, StringComparison.Ordinal))
            {
                return HealthCheckResult.Unhealthy("Data protection roundtrip failed: content mismatch.");
            }

            _logger.HealthCheckSucceeded();

            return HealthCheckResult.Healthy($"Redis ping: {pingResult.TotalMilliseconds}ms, Data protection: OK");
        }
        // CA1031: Health checks should catch all exceptions to report unhealthy status
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.HealthCheckFailed(ex);
            return HealthCheckResult.Unhealthy("Data protection health check failed.", ex);
        }
    }
}
