using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.ReliableMessaging.Inbox;
using Ruya.Services.ReliableMessaging.Outbox;
using Testcontainers.MsSql;

namespace Ruya.Services.ReliableMessaging.EntityFrameworkCore.Integration.Tests;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class SqlServerProviderContractTests
{
	private const string DatabaseName = "RuyaReliableMessagingTests";
	private static MsSqlContainer? _container;
	private static string _connectionString = null!;

	[ClassInitialize]
	public static async Task ClassInitialize(TestContext testContext)
	{
		ArgumentNullException.ThrowIfNull(testContext);

		_container = new MsSqlBuilder("cilerler/mssql-server-linux:2025-RTM-ubuntu-22.04")
			.WithPassword("PasswordAdmin1!")
			.Build();

		await _container.StartAsync().ConfigureAwait(false);

		_connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
		{
			InitialCatalog = DatabaseName,
		}.ConnectionString;

		await using var context = CreateContext();
		await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
	}

	[ClassCleanup]
	public static async Task ClassCleanup()
	{
		if (_container is not null)
		{
			await _container.DisposeAsync().ConfigureAwait(false);
		}
	}

	[TestInitialize]
	public async Task InitializeTestAsync()
	{
		await using var context = CreateContext();
		await context.Outbox.ExecuteDeleteAsync().ConfigureAwait(false);
		await context.Inbox.ExecuteDeleteAsync().ConfigureAwait(false);
		await context.BusinessRecords.ExecuteDeleteAsync().ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SaveChangesAsync_BusinessAndOutboxWrites_ShareSqlServerTransaction()
	{
		await using var services = CreateOutboxServiceProvider();
		await using var scope = services.CreateAsyncScope();
		var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
		var buffer = scope.ServiceProvider.GetRequiredService<IOutboxBuffer<TestDbContext>>();

		context.BusinessRecords.Add(new TestBusinessRecord { Id = 101, Value = "committed" });
		buffer.Add(NewEnvelope("sqlserver.transaction"));

		await context.SaveChangesAsync().ConfigureAwait(false);

		await using var verification = CreateContext();
		Assert.AreEqual(1, await verification.BusinessRecords.AsNoTracking().CountAsync().ConfigureAwait(false));
		Assert.AreEqual(1, await verification.Outbox.AsNoTracking().CountAsync().ConfigureAwait(false));
	}

	[TestMethod]
	public async Task SaveChangesAsync_BusinessConstraintFailure_RollsBackSqlServerOutboxWrite()
	{
		await using (var setup = CreateContext())
		{
			setup.BusinessRecords.Add(new TestBusinessRecord { Id = 201, Value = "existing" });
			await setup.SaveChangesAsync().ConfigureAwait(false);
		}

		await using var services = CreateOutboxServiceProvider();
		await using var scope = services.CreateAsyncScope();
		var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
		var buffer = scope.ServiceProvider.GetRequiredService<IOutboxBuffer<TestDbContext>>();

		context.BusinessRecords.Add(new TestBusinessRecord { Id = 201, Value = "duplicate" });
		buffer.Add(NewEnvelope("sqlserver.rollback"));

		await Assert.ThrowsExactlyAsync<DbUpdateException>(async () =>
			await context.SaveChangesAsync().ConfigureAwait(false));

		await using var verification = CreateContext();
		Assert.AreEqual(1, await verification.BusinessRecords.AsNoTracking().CountAsync().ConfigureAwait(false));
		Assert.AreEqual(0, await verification.Outbox.AsNoTracking().CountAsync().ConfigureAwait(false));
	}

	[TestMethod]
	public async Task TryRecordAsync_ConcurrentSqlServerContexts_EnforcesCompositeUniqueness()
	{
		await using var firstContext = CreateContext();
		await using var secondContext = CreateContext();
		var firstStore = new EntityFrameworkInboxStore<TestDbContext>(firstContext);
		var secondStore = new EntityFrameworkInboxStore<TestDbContext>(secondContext);
		var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		async Task<bool> RecordAsync(EntityFrameworkInboxStore<TestDbContext> store)
		{
			await start.Task.ConfigureAwait(false);
			return await store.TryRecordAsync(
				"consumer.sqlserver.unique",
				"message-sqlserver-unique",
				"topic.sqlserver.unique",
				CancellationToken.None).ConfigureAwait(false);
		}

		var first = RecordAsync(firstStore);
		var second = RecordAsync(secondStore);
		start.SetResult();
		var results = await Task.WhenAll(first, second).ConfigureAwait(false);

		Assert.AreEqual(1, results.Count(static recorded => recorded));
		Assert.AreEqual(1, results.Count(static recorded => !recorded));

		await using var verification = CreateContext();
		Assert.AreEqual(1, await verification.Inbox.AsNoTracking().CountAsync().ConfigureAwait(false));
	}

	[TestMethod]
	public async Task ExecuteOnceAsync_AbandonedSqlServerWork_RollsBackBusinessAndInbox()
	{
		await using var context = CreateContext();
		var store = new EntityFrameworkInboxStore<TestDbContext>(context);

		var result = await store.ExecuteOnceAsync(
			"consumer.sqlserver.rollback",
			"message-sqlserver-rollback",
			"topic.sqlserver.rollback",
			async cancellationToken =>
			{
				context.BusinessRecords.Add(new TestBusinessRecord { Id = 301, Value = "rolled-back" });
				await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				return InboxWorkResult.Abandoned;
			},
			CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(InboxExecutionResult.Abandoned, result);

		await using var verification = CreateContext();
		Assert.AreEqual(0, await verification.BusinessRecords.AsNoTracking().CountAsync().ConfigureAwait(false));
		Assert.AreEqual(0, await verification.Inbox.AsNoTracking().CountAsync().ConfigureAwait(false));
	}

	[TestMethod]
	[SuppressMessage(
		"Reliability",
		"CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
		Justification = "Both concurrent operations are awaited by Task.WhenAll before either DbContext leaves scope.")]
	public async Task ExecuteOnceAsync_ConcurrentSqlServerContexts_CommitsOneHandlerPath()
	{
		await using var firstContext = CreateContext();
		await using var secondContext = CreateContext();
		var firstStore = new EntityFrameworkInboxStore<TestDbContext>(firstContext);
		var secondStore = new EntityFrameworkInboxStore<TestDbContext>(secondContext);
		var callbackInvocations = 0;
		var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		async Task<InboxExecutionResult> ExecuteAsync(
			EntityFrameworkInboxStore<TestDbContext> store,
			TestDbContext context)
		{
			await start.Task.ConfigureAwait(false);
			return await store.ExecuteOnceAsync(
				"consumer.sqlserver.concurrent",
				"message-sqlserver-concurrent",
				"topic.sqlserver.concurrent",
				async cancellationToken =>
				{
					Interlocked.Increment(ref callbackInvocations);
					context.BusinessRecords.Add(new TestBusinessRecord { Id = 401, Value = "committed-once" });
					await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
					return InboxWorkResult.Processed;
				},
				CancellationToken.None).ConfigureAwait(false);
		}

		var first = ExecuteAsync(firstStore, firstContext);
		var second = ExecuteAsync(secondStore, secondContext);
		start.SetResult();
		var results = await Task.WhenAll(first, second).ConfigureAwait(false);

		Assert.AreEqual(1, results.Count(static result => result == InboxExecutionResult.Processed));
		Assert.AreEqual(1, results.Count(static result => result == InboxExecutionResult.Duplicate));
		Assert.AreEqual(1, callbackInvocations);

		await using var verification = CreateContext();
		Assert.AreEqual(1, await verification.BusinessRecords.AsNoTracking().CountAsync().ConfigureAwait(false));
		Assert.AreEqual(1, await verification.Inbox.AsNoTracking().CountAsync().ConfigureAwait(false));
		Assert.AreEqual(
			InboxStatus.Processed,
			(await verification.Inbox.AsNoTracking().SingleAsync().ConfigureAwait(false)).Status);
	}

	private static TestDbContext CreateContext()
	{
		var options = new DbContextOptionsBuilder<TestDbContext>()
			.UseSqlServer(_connectionString)
			.Options;

		return new TestDbContext(options);
	}

	private static ServiceProvider CreateOutboxServiceProvider()
	{
		var services = new ServiceCollection();
		services.AddScoped<IOutboxBuffer<TestDbContext>, OutboxBuffer<TestDbContext>>();
		services.AddSingleton<OutboxSavingChangesInterceptor<TestDbContext>>();
		services.AddDbContext<TestDbContext>((serviceProvider, options) =>
		{
			options.UseSqlServer(_connectionString);
			options.UseReliableMessagingOutbox<TestDbContext>(serviceProvider);
		});

		return services.BuildServiceProvider();
	}

	private static ReliableMessageEnvelope NewEnvelope(string topic) => new()
	{
		Topic = topic,
		PayloadJson = "{}",
		PayloadType = typeof(object).AssemblyQualifiedName!,
	};
}
