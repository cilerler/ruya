using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ruya.Services.DistributedLock.Abstractions;

namespace Ruya.Services.DistributedLock.HealthChecks;

/// <summary>
/// Health check for the distributed lock manager.
/// Verifies that the lock provider is responsive and functional.
/// </summary>
public sealed class DistributedLockHealthCheck : IHealthCheck
{
    private readonly IDistributedLockProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedLockHealthCheck"/> class.
    /// </summary>
    /// <param name="provider">The distributed lock provider to check.</param>
    public DistributedLockHealthCheck(IDistributedLockProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to acquire and release a health check lock
            var testKey = $"health-check-lock-{Guid.NewGuid()}";
            var testValue = Guid.NewGuid().ToString();

            var acquired = await _provider.AcquireLockAsync(
                testKey,
                testValue,
                TimeSpan.FromSeconds(10),
                cancellationToken);

            if (!acquired)
            {
                return HealthCheckResult.Degraded(
                    "Lock provider responded but could not acquire test lock. " +
                    "This may indicate high contention or provider issues.");
            }

            // Verify we can extend the lock
            var extended = await _provider.ExtendLockAsync(
                testKey,
                testValue,
                TimeSpan.FromSeconds(10),
                cancellationToken);

            if (!extended)
            {
                // Still try to release
                await _provider.ReleaseLockAsync(testKey, testValue, cancellationToken);

                return HealthCheckResult.Degraded(
                    "Lock provider could not extend test lock. " +
                    "This may indicate timing or synchronization issues.");
            }

            // Release the lock
            var released = await _provider.ReleaseLockAsync(
                testKey,
                testValue,
                cancellationToken);

            if (!released)
            {
                return HealthCheckResult.Degraded(
                    "Lock provider could not release test lock. " +
                    "This may indicate state management issues.");
            }

            return HealthCheckResult.Healthy(
                "Lock provider is responsive and all operations succeeded.");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "Health check was cancelled before completing.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Lock provider error during health check.",
                ex);
        }
    }
}
