using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore.Integration.Tests;

/// <summary>
/// Base class providing an isolated in-memory SQLite database per test for provider-neutral store behavior.
/// SQL Server transaction, constraint, rollback, and concurrency semantics are verified separately through
/// Testcontainers in <see cref="SqlServerProviderContractTests"/>.
/// </summary>
public abstract class IntegrationTestBase
{
	protected SqliteConnection Connection { get; private set; } = null!;
	protected TestDbContext Db { get; private set; } = null!;

	protected virtual async Task InitializeAsync()
	{
		Connection = new SqliteConnection("Data Source=:memory:");
		await Connection.OpenAsync();

		var options = new DbContextOptionsBuilder<TestDbContext>()
			.UseSqlite(Connection)
			.Options;

		Db = new TestDbContext(options);
		await Db.Database.EnsureCreatedAsync();
	}

	protected virtual async Task CleanupAsync()
	{
		if (Db is not null)
		{
			await Db.DisposeAsync();
		}

		if (Connection is not null)
		{
			await Connection.DisposeAsync();
		}
	}
}
