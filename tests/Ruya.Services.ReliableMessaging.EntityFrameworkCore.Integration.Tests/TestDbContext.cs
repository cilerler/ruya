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
	public DbSet<TestBusinessRecord> BusinessRecords => Set<TestBusinessRecord>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.ApplyOutboxEntryConfiguration(OutboxOptions);
		modelBuilder.ApplyInboxEntryConfiguration(InboxOptions);
		modelBuilder.Entity<TestBusinessRecord>(entity =>
		{
			entity.ToTable("BusinessRecord");
			entity.HasKey(record => record.Id);
			entity.Property(record => record.Id).ValueGeneratedNever();
			entity.Property(record => record.Value).IsRequired();
		});
	}
}

public sealed class TestBusinessRecord
{
	public int Id { get; set; }
	public string Value { get; set; } = null!;
}
