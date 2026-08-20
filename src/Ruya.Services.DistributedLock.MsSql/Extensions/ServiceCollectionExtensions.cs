using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Extensions;
using Ruya.Services.DistributedLock.MsSql.Configuration;
using Ruya.Services.DistributedLock.MsSql.Providers;

namespace Ruya.Services.DistributedLock.MsSql.Extensions;

/// <summary>
/// Extension methods for configuring SQL Server-based distributed lock services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL Server-based lock manager.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlServerDistributedLock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register core services
        services.AddDistributedLockCore();

        // Register SQL Server settings
        services.AddOptions<SqlServerLockSettings>()
            .BindConfiguration(SqlServerLockSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.ConnectionStringKey),
                "ConnectionStringKey is required.")
            .Validate<IConfiguration>(
                (settings, configuration) =>
                    !string.IsNullOrWhiteSpace(
                        configuration.GetConnectionString(settings.ConnectionStringKey)),
                "The configured SQL Server connection-string catalog entry is required.")
            .ValidateOnStart();

        // Register SQL Server provider
        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var sqlSettings = sp.GetRequiredService<IOptions<SqlServerLockSettings>>().Value;
            var configuration = sp.GetRequiredService<IConfiguration>();
            string connectionString = configuration.GetConnectionString(sqlSettings.ConnectionStringKey)
                ?? throw new InvalidOperationException(
                    $"Connection string catalog entry '{sqlSettings.ConnectionStringKey}' is not configured.");
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new SqlServerLockProvider(
                connectionString,
                loggerFactory.CreateLogger<SqlServerLockProvider>());
        });

        return services;
    }
}
