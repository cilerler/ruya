using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Settings are bound from the <c>DistributedLock</c> configuration section and validated at startup.
    /// </remarks>
    public static IServiceCollection AddDistributedLockCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<DistributedLockSettings> options = services.AddOptions<DistributedLockSettings>();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IConfiguration)))
        {
            options.BindConfiguration(DistributedLockSettings.ConfigurationSectionName);
        }

        options.ValidateDataAnnotations()
            .Validate(
                settings => settings.InstanceName is null ||
                    (!string.IsNullOrWhiteSpace(settings.InstanceName) &&
                     settings.InstanceName.Length <= Ruya.Services.DistributedLock.Common.LockValidation.MaxLockKeyLength - 2),
                $"InstanceName must be nonblank and no longer than {Ruya.Services.DistributedLock.Common.LockValidation.MaxLockKeyLength - 2} characters when configured.")
            .ValidateOnStart();

        services.TryAddTransient<IDistributedLock, CoreDistributedLock>();
        services.TryAddSingleton<DistributedLockMetrics>();

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
    /// - lock_release_failed_total: Counter of lock releases that could not be confirmed
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

        services.Replace(ServiceDescriptor.Singleton(_ => new DistributedLockMetrics(meterName)));

        return services;
    }
}
