using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore;

/// <summary>
/// Convenience extensions on <see cref="DbContextOptionsBuilder"/> to attach the reliable-messaging interceptors.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
	/// <summary>
	/// Attaches the <see cref="OutboxSavingChangesInterceptor{TDbContext}"/> so the outbox buffer is drained
	/// inside every <c>SaveChangesAsync</c>. Call from your <c>AddDbContext</c> registration.
	/// </summary>
	/// <typeparam name="TDbContext">The concrete <see cref="DbContext"/> type.</typeparam>
	/// <param name="optionsBuilder">The builder being configured.</param>
	/// <param name="serviceProvider">The application service provider (the <c>(sp, options) =&gt; ...</c> overload of <c>AddDbContext</c>).</param>
	public static DbContextOptionsBuilder UseReliableMessagingOutbox<TDbContext>(
		this DbContextOptionsBuilder optionsBuilder,
		IServiceProvider serviceProvider)
		where TDbContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(optionsBuilder);
		ArgumentNullException.ThrowIfNull(serviceProvider);

		optionsBuilder.AddInterceptors(
			serviceProvider.GetRequiredService<OutboxSavingChangesInterceptor<TDbContext>>());

		return optionsBuilder;
	}
}
