using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Ruya.EntityFrameworkCore.ModelMetadata;

public static class StartupExtensions
{
	/// <summary>
	/// Adds ModelMetadataService that resolves DbContext from DI.
	/// Requires TContext to be registered via AddDbContext.
	/// </summary>
	public static IServiceCollection AddModelMetadataService<TContext>(this IServiceCollection serviceCollection) where TContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);
		serviceCollection.AddSingleton<IModelMetadata, ModelMetadataService<TContext>>();
		return serviceCollection;
	}

	/// <summary>
	/// Adds ModelMetadataService without requiring DbContext in DI.
	/// Creates DbContext internally using an empty SQL Server connection to build the model in memory.
	/// TContext must have a constructor that accepts DbContextOptions&lt;TContext&gt;.
	/// </summary>
	public static IServiceCollection AddModelMetadataServiceStandalone<TContext>(this IServiceCollection serviceCollection, string defaultSchema = ModelMetadataService<TContext>.DefaultSchema) where TContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);
		serviceCollection.AddSingleton<IModelMetadata>(new ModelMetadataService<TContext>(defaultSchema));
		return serviceCollection;
	}
}
