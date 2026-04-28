using System;
using Microsoft.EntityFrameworkCore;
using Ruya.Services.ReliableMessaging.Inbox;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="ModelBuilder"/> extensions that map <see cref="OutboxEntry"/> and <see cref="InboxEntry"/>
/// into the caller's <c>DbContext</c>. Call from <c>OnModelCreating</c>.
/// </summary>
public static class ModelBuilderExtensions
{
	/// <summary>Maps <see cref="OutboxEntry"/> as a table with indices suited to the dispatch path.</summary>
	public static ModelBuilder ApplyOutboxEntryConfiguration(
		this ModelBuilder modelBuilder,
		EntityFrameworkOutboxStoreOptions options)
	{
		ArgumentNullException.ThrowIfNull(modelBuilder);
		ArgumentNullException.ThrowIfNull(options);

		var entity = modelBuilder.Entity<OutboxEntry>();
		entity.ToTable(options.TableName, options.SchemaName);
		entity.HasKey(e => e.Id);

		entity.Property(e => e.Topic).HasMaxLength(255).IsRequired();
		entity.Property(e => e.DispatcherName).HasMaxLength(64);
		entity.Property(e => e.PayloadType).HasMaxLength(512).IsRequired();
		entity.Property(e => e.PayloadJson).IsRequired();
		entity.Property(e => e.HeadersJson);
		entity.Property(e => e.LastError);
		entity.Property(e => e.Status).HasConversion<byte>();

		entity.HasIndex(e => new { e.Status, e.NextAttemptAt })
			.HasDatabaseName("IX_Outbox_Dispatch");

		entity.HasIndex(e => e.EnqueuedAt)
			.HasDatabaseName("IX_Outbox_EnqueuedAt");

		return modelBuilder;
	}

	/// <summary>Maps <see cref="InboxEntry"/> with the composite <c>(ConsumerName, MessageId)</c> PK that enforces dedup.</summary>
	public static ModelBuilder ApplyInboxEntryConfiguration(
		this ModelBuilder modelBuilder,
		EntityFrameworkInboxStoreOptions options)
	{
		ArgumentNullException.ThrowIfNull(modelBuilder);
		ArgumentNullException.ThrowIfNull(options);

		var entity = modelBuilder.Entity<InboxEntry>();
		entity.ToTable(options.TableName, options.SchemaName);
		entity.HasKey(e => new { e.ConsumerName, e.MessageId });

		entity.Property(e => e.ConsumerName).HasMaxLength(256).IsRequired();
		entity.Property(e => e.MessageId).HasMaxLength(64).IsRequired();
		entity.Property(e => e.Topic).HasMaxLength(255).IsRequired();
		entity.Property(e => e.Status).HasConversion<byte>();

		entity.HasIndex(e => e.ReceivedAt)
			.HasDatabaseName("IX_Inbox_ReceivedAt");

		return modelBuilder;
	}
}
