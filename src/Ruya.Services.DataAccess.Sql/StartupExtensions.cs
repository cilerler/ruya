using System;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Services.DataAccess.Abstractions;

namespace Ruya
{
    public static partial class StartupExtensions
	{
		public static IServiceCollection AddSql(this IServiceCollection serviceCollection)
		{
			if (serviceCollection == null)
			{
				throw new ArgumentNullException(nameof(serviceCollection));
			}

			serviceCollection.AddTransient<ISqlClient, Client>();
			return serviceCollection;
		}

	}
}
