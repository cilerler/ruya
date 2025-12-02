using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ruya.EntityFrameworkCore.ModelMetadata;

public static class StartupExtensions
{
	public static IServiceCollection AddModelMetadataService<TContext>(this IServiceCollection serviceCollection) where TContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);
		serviceCollection.AddSingleton<IModelMetadata, ModelMetadataService<TContext>>();
		return serviceCollection;
	}
}
