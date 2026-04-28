using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore.Integration.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class OutboxSavingChangesInterceptorTests
{
	// This suite wires the interceptor through the application service provider (as real callers do)
	// because OutboxSavingChangesInterceptor<T>.Flush resolves IOutboxBuffer<T> from DbContext.GetService<>.
	private ServiceProvider _services = null!;
	private SqliteConnection _connection = null!;

	[TestInitialize]
	public async Task InitAsync()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		await _connection.OpenAsync();

		var services = new ServiceCollection();
		services.AddScoped<IOutboxBuffer<TestDbContext>, OutboxBuffer<TestDbContext>>();
		services.AddSingleton<OutboxSavingChangesInterceptor<TestDbContext>>();
		services.AddDbContext<TestDbContext>((sp, options) =>
		{
			options.UseSqlite(_connection);
			options.UseReliableMessagingOutbox<TestDbContext>(sp);
		});

		_services = services.BuildServiceProvider();

		using var scope = _services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
		await db.Database.EnsureCreatedAsync();
	}

	[TestCleanup]
	public async Task CleanupAsync()
	{
		await _services.DisposeAsync();
		await _connection.DisposeAsync();
	}

	[TestMethod]
	public async Task SaveChangesAsync_WithBufferedEnvelopes_PersistsOutboxRowsInSameTransaction()
	{
		using var scope = _services.CreateScope();
		var buffer = scope.ServiceProvider.GetRequiredService<IOutboxBuffer<TestDbContext>>();
		var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

		buffer.Add(NewEnvelope("topic.one"));
		buffer.Add(NewEnvelope("topic.two"));

		await db.SaveChangesAsync();

		var stored = await db.Outbox.AsNoTracking().OrderBy(e => e.Topic).ToListAsync();
		Assert.AreEqual(2, stored.Count);
		Assert.AreEqual("topic.one", stored[0].Topic);
		Assert.AreEqual("topic.two", stored[1].Topic);
		Assert.AreEqual(0, buffer.Count); // buffer drained
	}

	[TestMethod]
	public async Task SaveChangesAsync_WithEmptyBuffer_WritesNoOutboxRows()
	{
		using var scope = _services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

		await db.SaveChangesAsync();

		var count = await db.Outbox.AsNoTracking().CountAsync();
		Assert.AreEqual(0, count);
	}

	[TestMethod]
	public async Task SaveChangesAsync_PerScope_BuffersAreIsolated()
	{
		// Scope A buffers one envelope.
		using (var scopeA = _services.CreateScope())
		{
			var bufferA = scopeA.ServiceProvider.GetRequiredService<IOutboxBuffer<TestDbContext>>();
			var dbA = scopeA.ServiceProvider.GetRequiredService<TestDbContext>();
			bufferA.Add(NewEnvelope("scope-a-topic"));
			await dbA.SaveChangesAsync();
		}

		// Scope B buffers a different envelope. Scope A's envelope should already be in the DB, scope B's should land separately.
		using (var scopeB = _services.CreateScope())
		{
			var bufferB = scopeB.ServiceProvider.GetRequiredService<IOutboxBuffer<TestDbContext>>();
			var dbB = scopeB.ServiceProvider.GetRequiredService<TestDbContext>();
			bufferB.Add(NewEnvelope("scope-b-topic"));
			await dbB.SaveChangesAsync();
		}

		using var verifyScope = _services.CreateScope();
		var db = verifyScope.ServiceProvider.GetRequiredService<TestDbContext>();
		var topics = await db.Outbox.AsNoTracking().Select(e => e.Topic).OrderBy(t => t).ToListAsync();
		CollectionAssert.AreEqual(new[] { "scope-a-topic", "scope-b-topic" }, topics);
	}

	private static ReliableMessageEnvelope NewEnvelope(string topic) => new()
	{
		Topic = topic,
		PayloadJson = "{}",
		PayloadType = typeof(object).AssemblyQualifiedName!,
	};
}
