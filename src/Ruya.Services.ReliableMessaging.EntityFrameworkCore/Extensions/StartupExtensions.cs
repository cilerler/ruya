using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ruya.Services.ReliableMessaging.Extensions;
using Ruya.Services.ReliableMessaging.Inbox;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore.Extensions;

/// <summary>DI registration surface for the EF Core storage adapter.</summary>
public static class StartupExtensions
{
	/// <summary>
	/// Registers the EF Core outbox store and the <see cref="OutboxSavingChangesInterceptor{TDbContext}"/> for
	/// <typeparamref name="TDbContext"/>. The interceptor must also be attached via
	/// <c>optionsBuilder.UseReliableMessagingOutbox&lt;TDbContext&gt;(sp)</c> in your <c>AddDbContext</c> call,
	/// and the <see cref="OutboxEntry"/> entity must be mapped via <c>ModelBuilder.ApplyOutboxEntryConfiguration</c>
	/// in <c>OnModelCreating</c>.
	/// </summary>
	public static IReliableMessagingBuilder AddEntityFrameworkOutboxStore<TDbContext>(this IReliableMessagingBuilder builder)
		where TDbContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.Services.TryAddScoped<IOutboxStore<TDbContext>, EntityFrameworkOutboxStore<TDbContext>>();
		builder.Services.TryAddSingleton<OutboxSavingChangesInterceptor<TDbContext>>();

		return builder;
	}

	/// <summary>
	/// Registers the EF Core inbox store for <typeparamref name="TDbContext"/>. The <see cref="InboxEntry"/> entity must be
	/// mapped via <c>ModelBuilder.ApplyInboxEntryConfiguration</c> in <c>OnModelCreating</c>.
	/// </summary>
	public static IReliableMessagingBuilder AddEntityFrameworkInboxStore<TDbContext>(this IReliableMessagingBuilder builder)
		where TDbContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.Services.TryAddScoped<EntityFrameworkInboxStore<TDbContext>>();
		builder.Services.TryAddScoped<IInboxStore<TDbContext>>(services =>
			services.GetRequiredService<EntityFrameworkInboxStore<TDbContext>>());
		builder.Services.TryAddScoped<IAtomicInboxStore<TDbContext>>(services =>
			services.GetRequiredService<EntityFrameworkInboxStore<TDbContext>>());

		return builder;
	}
}
