using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Configuration;
using Ruya.Services.DistributedLock.HealthChecks;
using Ruya.Services.DistributedLock.Telemetry;
using CoreDistributedLock = Ruya.Services.DistributedLock.Core.DistributedLock;

namespace Ruya.Services.DistributedLock.Extensions;

/// <summary>
/// Extension methods for configuring distributed lock services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds distributed lock manager core services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This overload registers only the IDistributedLock service. Settings must be configured separately.
    /// Use this when configuring settings programmatically or when using provider-specific registration methods.
    /// </remarks>
    public static IServiceCollection AddDistributedLockCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register lock manager
        services.TryAddTransient<IDistributedLock, CoreDistributedLock>();

        return services;
    }

    /// <summary>
    /// Adds distributed lock manager core services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDistributedLockCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Register core services
        services.AddDistributedLockCore();

        // Register settings
        services.AddOptions<DistributedLockSettings>()
            .Bind(configuration.GetSection(DistributedLockSettings.ConfigurationSectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Adds health check for the lock manager.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name. Defaults to "lock-manager".</param>
    /// <param name="failureStatus">The status to report when the health check fails.</param>
    /// <param name="tags">Tags for the health check.</param>
    /// <returns>The health checks builder for chaining.</returns>
    public static IHealthChecksBuilder AddDistributedLockHealthCheck(
        this IHealthChecksBuilder builder,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<DistributedLockHealthCheck>(
            name ?? "lock-manager",
            failureStatus,
            tags ?? []);
    }

    /// <summary>
    /// Adds metrics/telemetry for the lock manager.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="meterName">Optional custom meter name. Defaults to "Ruya.Services.DistributedLock".</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Metrics are exposed via System.Diagnostics.Metrics and can be collected by
    /// OpenTelemetry, Prometheus, or other metrics exporters.
    ///
    /// Available metrics:
    /// - lock_acquired_total: Counter of successful lock acquisitions
    /// - lock_failed_total: Counter of failed lock acquisitions
    /// - lock_released_total: Counter of successful lock releases
    /// - lock_duration_ms: Histogram of lock hold times
    /// - lock_acquisition_duration_ms: Histogram of lock acquisition times
    /// - heartbeat_success_total: Counter of successful heartbeat extensions
    /// - heartbeat_failure_total: Counter of failed heartbeat extensions
    /// - active_locks: Gauge of currently active locks
    /// </remarks>
    public static IServiceCollection AddDistributedLockMetrics(
        this IServiceCollection services,
        string? meterName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp => new DistributedLockMetrics(meterName));

        return services;
    }
}
