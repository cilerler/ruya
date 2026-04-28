using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore;

/// <summary>
/// EF Core interceptor that drains <see cref="IOutboxBuffer{TContext}"/> during <c>SaveChangesAsync</c>,
/// appending <see cref="OutboxEntry"/> rows to the same <see cref="DbContext"/> so that business state and
/// outbox state commit in one transaction.
/// </summary>
/// <typeparam name="TDbContext">Concrete <see cref="DbContext"/> type the interceptor is bound to.</typeparam>
public sealed class OutboxSavingChangesInterceptor<TDbContext> : SaveChangesInterceptor
	where TDbContext : DbContext
{
	public override InterceptionResult<int> SavingChanges(
		DbContextEventData eventData,
		InterceptionResult<int> result)
	{
		ArgumentNullException.ThrowIfNull(eventData);
		Flush(eventData.Context);
		return result;
	}

	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(eventData);
		Flush(eventData.Context);
		return new ValueTask<InterceptionResult<int>>(result);
	}

	private static void Flush(DbContext? context)
	{
		if (context is null)
		{
			return;
		}

		if (context is not TDbContext)
		{
			return; // interceptor scoped to TDbContext only
		}

		var buffer = context.GetService<IOutboxBuffer<TDbContext>>();
		var envelopes = buffer.Drain();
		if (envelopes.Count == 0)
		{
			return;
		}

		var dbSet = context.Set<OutboxEntry>();
		foreach (var envelope in envelopes)
		{
			dbSet.Add(ToEntry(envelope));
		}
	}

	private static OutboxEntry ToEntry(ReliableMessageEnvelope envelope)
	{
		return new OutboxEntry
		{
			Id = envelope.MessageId,
			Topic = envelope.Topic,
			DispatcherName = envelope.DispatcherName,
			PayloadJson = envelope.PayloadJson,
			PayloadType = envelope.PayloadType,
			HeadersJson = envelope.Headers is null
				? null
				: JsonSerializer.Serialize((IReadOnlyDictionary<string, string>)envelope.Headers),
			EnqueuedAt = envelope.EnqueuedAt,
			NextAttemptAt = envelope.EnqueuedAt,
			AttemptCount = 0,
			Status = OutboxStatus.Pending,
		};
	}
}
