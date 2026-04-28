namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore;

/// <summary>
/// Schema + table naming for the inbox entity mapped by <c>ModelBuilder.ApplyInboxEntryConfiguration</c>.
/// Pass an instance from <c>OnModelCreating</c>; per-context instances are typically constructed inline there.
/// </summary>
public sealed class EntityFrameworkInboxStoreOptions
{
	/// <summary>Database schema that owns the inbox table. <see langword="null"/> uses the context's default schema.</summary>
	public string? SchemaName { get; init; }

	/// <summary>Name of the inbox table.</summary>
	public string TableName { get; init; } = "Inbox";
}
