using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ruya.EntityFrameworkCore.SqlServer.BatchLock;

public static class StartupExtensions
{
	/// <summary>
	/// Registers batch lock operations services for SQL Server.
	/// Uses SELECT FOR UPDATE pattern with ROWLOCK, UPDLOCK, READPAST hints.
	/// </summary>
	/// <typeparam name="TContext">The DbContext type to use for database operations.</typeparam>
	/// <param name="serviceCollection">The service collection.</param>
	/// <returns>The service collection for chaining.</returns>
	public static IServiceCollection AddBatchLockOperations<TContext>(this IServiceCollection serviceCollection)
		where TContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);

		serviceCollection.TryAddScoped<IBatchLockOperations, BatchLockOperations<TContext>>();

		return serviceCollection;
	}
}
