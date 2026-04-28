namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore;

/// <summary>
/// Schema + table naming for the outbox entity mapped by <c>ModelBuilder.ApplyOutboxEntryConfiguration</c>.
/// Pass an instance from <c>OnModelCreating</c>; per-context instances are typically constructed inline there.
/// </summary>
public sealed class EntityFrameworkOutboxStoreOptions
{
	/// <summary>Database schema that owns the outbox table. <see langword="null"/> uses the context's default schema.</summary>
	public string? SchemaName { get; init; }

	/// <summary>Name of the outbox table.</summary>
	public string TableName { get; init; } = "Outbox";
}
