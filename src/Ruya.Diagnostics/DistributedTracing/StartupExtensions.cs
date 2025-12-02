using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ruya.Diagnostics.DistributedTracing;

public static class StartupExtensions
{
    /// <summary>
    /// Adds distributed tracing services with configuration validation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="activitySourceName">Name for the ActivitySource. Defaults to entry assembly name.</param>
    /// <param name="activitySourceVersion">Version for the ActivitySource. Defaults to entry assembly version.</param>
    /// <returns>The service collection for chaining.</returns>
	public static IServiceCollection AddDistributedTracingService(
		this IServiceCollection serviceCollection,
        string? activitySourceName = null,
        string? activitySourceVersion = null)
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);

        var requiredServices = new[]
        {
            typeof(IDistributedCache),
        };
        var missingServices = requiredServices
            .Where(t => !serviceCollection.Any(sd => sd.ServiceType == t))
            .ToList();
        if (missingServices.Count > 0)
        {
            throw new InvalidOperationException($"Missing required services: {string.Join(", ", missingServices.Select(t => t.Name))}");
        }

		serviceCollection.AddOptions<DistributedTracingSettings>()
			.Configure<IConfiguration>((settings, configuration) =>
				{
					ArgumentNullException.ThrowIfNull(configuration);
					var section = configuration.GetSection(DistributedTracingSettings.ConfigurationSectionName);
					ArgumentNullException.ThrowIfNull(section.Exists() ? string.Empty : null, DistributedTracingSettings.ConfigurationSectionName);
					section.Bind(settings);
				})
			.ValidateDataAnnotations()
			.ValidateOnStart();

        var sourceName = activitySourceName ?? Primitives.Startup.AssemblyName;
        var sourceVersion = activitySourceVersion ?? Primitives.Startup.AssemblyVersion;
        serviceCollection.TryAddSingleton(_ => new ActivitySource(sourceName, sourceVersion));
		serviceCollection.AddSingleton<IDistributedTracing, DistributedTracingService>();

		return serviceCollection;
	}
}
