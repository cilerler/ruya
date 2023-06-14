using System;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Services.DataAccess.Abstractions;
using Ruya.Services.DataAccess.Sql;

// ReSharper disable once CheckNamespace
namespace Ruya;

public static class StartupExtensions
{
	public static IServiceCollection AddSql(this IServiceCollection serviceCollection)
	{
		if (serviceCollection == null) throw new ArgumentNullException(nameof(serviceCollection));

		serviceCollection.AddTransient<ISqlClient, Client>();
		return serviceCollection;
	}
}
