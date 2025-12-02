using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.EntityFrameworkCore.ModelMetadata;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

namespace Ruya.EntityFrameworkCore.SqlServer;

public static class StartupExtensions
{
    /// <summary>
    /// Registers bulk insert operations services for SQL Server.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
   public static IServiceCollection AddBulkInsertOperations<TContext>(this IServiceCollection serviceCollection) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        if (!serviceCollection.Any(x => x.ServiceType == typeof(IDistributedTracing)))
        {
            throw new InvalidOperationException($"IDistributedTracing is not registered. Please register it before calling {nameof(AddBulkInsertOperations)}.");
        }

		serviceCollection.AddOptions<BulkInsertOperationsSettings>()
		.ValidateDataAnnotations()
		.ValidateOnStart()
		.Configure<IConfiguration>((settings, configuration) =>
		{
			ArgumentNullException.ThrowIfNull(configuration);
			var section = configuration.GetSection(BulkInsertOperationsSettings.ConfigurationSectionName);
#pragma warning disable S3236 // Caller information arguments should not be provided explicitly
			ArgumentNullException.ThrowIfNull(section.Exists() ? string.Empty : null, BulkInsertOperationsSettings.ConfigurationSectionName);
#pragma warning restore S3236 // Caller information arguments should not be provided explicitly
			section.Bind(settings);
		});

        serviceCollection.TryAddSingleton<IModelMetadata, ModelMetadataService<TContext>>();
        serviceCollection.TryAddSingleton<IBulkInsertOperations, BulkInsertOperations>();

        return serviceCollection;
    }
}
