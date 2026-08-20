using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.EntityFrameworkCore.ModelMetadata;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;
using Ruya.Extensions.DependencyInjection;

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

        serviceCollection.EnsureServicesRegistered(typeof(IDistributedTracing));

		serviceCollection.AddOptions<BulkInsertOperationsSettings>()
		.BindConfiguration(BulkInsertOperationsSettings.ConfigurationSectionName)
		.ValidateDataAnnotations()
		.Validate<IConfiguration>(
			static (_, configuration) => configuration.GetSection(BulkInsertOperationsSettings.ConfigurationSectionName).Exists(),
			$"Configuration section '{BulkInsertOperationsSettings.ConfigurationSectionName}' is required.")
		.ValidateOnStart();

        serviceCollection.TryAddSingleton<IModelMetadata, ModelMetadataService<TContext>>();
        serviceCollection.TryAddSingleton<IBulkInsertOperations, BulkInsertOperations>();

        return serviceCollection;
    }
}
