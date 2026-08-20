using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ruya.Extensions.DependencyInjection;

namespace Ruya.Diagnostics.DistributedTracing;

public static class StartupExtensions
{
    /// <summary>
    /// Adds distributed tracing services with configuration validation.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="activitySourceName">Name for the ActivitySource. Defaults to entry assembly name.</param>
    /// <param name="activitySourceVersion">Version for the ActivitySource. Defaults to entry assembly version.</param>
    /// <returns>The service collection for chaining.</returns>
	public static IServiceCollection AddDistributedTracingService(
		this IServiceCollection serviceCollection,
        string? activitySourceName = null,
        string? activitySourceVersion = null)
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.EnsureServicesRegistered(typeof(IDistributedCache), typeof(IMeterFactory));

		serviceCollection.AddOptions<DistributedTracingSettings>()
			.BindConfiguration(DistributedTracingSettings.ConfigurationSectionName)
			.ValidateDataAnnotations()
			.Validate<IConfiguration>(
				(_, configuration) => configuration.GetSection(DistributedTracingSettings.ConfigurationSectionName).Exists(),
				$"Configuration section '{DistributedTracingSettings.ConfigurationSectionName}' is required.")
			.Validate(
				settings => settings.CacheAbsoluteExpiration is null ||
					settings.CacheAbsoluteExpiration >= settings.CacheSlidingExpiration,
				"CacheAbsoluteExpiration must be greater than or equal to CacheSlidingExpiration.")
			.Validate(
				settings => settings.DefaultTags is not null && settings.DefaultTags.All(tag =>
					!string.IsNullOrWhiteSpace(tag.Key) && !string.IsNullOrWhiteSpace(tag.Value)),
				"DefaultTags keys and values must be nonblank.")
			.ValidateOnStart();

        var sourceName = activitySourceName ?? Primitives.Startup.AssemblyName;
        var sourceVersion = activitySourceVersion ?? Primitives.Startup.AssemblyVersion;
        serviceCollection.TryAddSingleton(_ => new ActivitySource(sourceName, sourceVersion));
		serviceCollection.AddSingleton<IDistributedTracing, DistributedTracingService>();

		return serviceCollection;
	}
}
