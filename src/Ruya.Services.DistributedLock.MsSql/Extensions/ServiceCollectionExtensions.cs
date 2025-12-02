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
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                ArgumentNullException.ThrowIfNull(configuration);
                var section = configuration.GetSection(SqlServerLockSettings.ConfigurationSectionName);
                ArgumentNullException.ThrowIfNull(section.Exists() ? string.Empty : null, SqlServerLockSettings.ConfigurationSectionName);
                section.Bind(settings);
                settings.ConnectionString = configuration.GetConnectionString(settings.ConnectionStringKey) ?? throw new ArgumentNullException(nameof(settings.ConnectionString));
            });

        // Register SQL Server provider
        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var sqlSettings = sp.GetRequiredService<IOptions<SqlServerLockSettings>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new SqlServerLockProvider(
                sqlSettings.ConnectionString,
                loggerFactory.CreateLogger<SqlServerLockProvider>());
        });

        return services;
    }
}
