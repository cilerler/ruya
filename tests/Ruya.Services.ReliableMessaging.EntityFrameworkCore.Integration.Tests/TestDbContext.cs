using Microsoft.EntityFrameworkCore;
using Ruya.Services.ReliableMessaging.EntityFrameworkCore;
using Ruya.Services.ReliableMessaging.Inbox;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore.Integration.Tests;

/// <summary>Minimal DbContext used by integration tests to exercise the Outbox/Inbox EF adapter.</summary>
public sealed class TestDbContext : DbContext
{
	public static readonly EntityFrameworkOutboxStoreOptions OutboxOptions = new()
	{
		SchemaName = null, // SQLite has no schemas; let the provider drop it
		TableName = "Outbox",
	};

	public static readonly EntityFrameworkInboxStoreOptions InboxOptions = new()
	{
		SchemaName = null,
		TableName = "Inbox",
	};

	public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

	public DbSet<OutboxEntry> Outbox => Set<OutboxEntry>();
	public DbSet<InboxEntry> Inbox => Set<InboxEntry>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.ApplyOutboxEntryConfiguration(OutboxOptions);
		modelBuilder.ApplyInboxEntryConfiguration(InboxOptions);
	}
}
