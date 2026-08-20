using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.DistributedLock.MsSql.Providers;
using Testcontainers.MsSql;

namespace Ruya.Services.DistributedLock.MsSql.Tests;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class SqlServerProviderContractTests
{
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
        _connectionString = _container.GetConnectionString();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task AcquireAndRelease_EnforcesSqlServerSessionOwnership()
    {
        using var owner = CreateProvider();
        using var contender = CreateProvider();
        string key = $"ownership-{Guid.NewGuid():N}";

        Assert.IsTrue(await owner.AcquireLockAsync(key, "owner", TimeSpan.FromSeconds(10)));
        Assert.IsFalse(await contender.AcquireLockAsync(key, "contender", TimeSpan.FromSeconds(10)));
        Assert.IsFalse(await owner.ReleaseLockAsync(key, "not-owner"));
        Assert.IsTrue(await owner.ReleaseLockAsync(key, "owner"));
        Assert.IsTrue(await contender.AcquireLockAsync(key, "contender", TimeSpan.FromSeconds(10)));
        Assert.IsTrue(await contender.ReleaseLockAsync(key, "contender"));
    }

    [TestMethod]
    public async Task ConcurrentAcquire_AllowsExactlyOneSqlServerOwner()
    {
        string key = $"concurrency-{Guid.NewGuid():N}";
        SqlServerLockProvider[] providers = Enumerable.Range(0, 8).Select(_ => CreateProvider()).ToArray();

        try
        {
            bool[] results = await Task.WhenAll(providers.Select((provider, index) =>
                provider.AcquireLockAsync(key, $"owner-{index}", TimeSpan.FromSeconds(10))));

            Assert.AreEqual(1, results.Count(acquired => acquired));
            int ownerIndex = Array.FindIndex(results, acquired => acquired);
            Assert.IsTrue(await providers[ownerIndex].ReleaseLockAsync(key, $"owner-{ownerIndex}"));
        }
        finally
        {
            foreach (SqlServerLockProvider provider in providers)
            {
                provider.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task ExtendLock_KeepsSqlServerLockPastOriginalExpiry()
    {
        using var owner = CreateProvider();
        using var contender = CreateProvider();
        string key = $"extend-{Guid.NewGuid():N}";

        Assert.IsTrue(await owner.AcquireLockAsync(key, "owner", TimeSpan.FromMilliseconds(800)));
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        Assert.IsTrue(await owner.ExtendLockAsync(key, "owner", TimeSpan.FromSeconds(2)));
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        Assert.IsFalse(await contender.AcquireLockAsync(key, "contender", TimeSpan.FromSeconds(10)));
        Assert.IsTrue(await owner.ReleaseLockAsync(key, "owner"));
    }

    [TestMethod]
    public async Task AcquireLock_WithPreCancelledToken_DoesNotCreateSqlServerLock()
    {
        using var cancelledProvider = CreateProvider();
        using var verificationProvider = CreateProvider();
        string key = $"cancel-{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cancelledProvider.AcquireLockAsync(key, "cancelled", TimeSpan.FromSeconds(10), cancellation.Token));

        Assert.IsTrue(await verificationProvider.AcquireLockAsync(key, "verification", TimeSpan.FromSeconds(10)));
        Assert.IsTrue(await verificationProvider.ReleaseLockAsync(key, "verification"));
    }

    [TestMethod]
    public async Task AcquireLock_WhenCancellationArrivesAfterGrant_ReleasesPooledSession()
    {
        using var cancellation = new CancellationTokenSource();
        using var cancelledProvider = new SqlServerLockProvider(
            _connectionString,
            NullLogger<SqlServerLockProvider>.Instance,
            TimeProvider.System,
            cancellation.Cancel);
        using var verificationProvider = CreateProvider();
        string key = $"post-grant-cancel-{Guid.NewGuid():N}";

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cancelledProvider.AcquireLockAsync(
                key,
                "cancelled",
                TimeSpan.FromSeconds(10),
                cancellation.Token));

        Assert.IsTrue(await verificationProvider.AcquireLockAsync(key, "verification", TimeSpan.FromSeconds(10)));
        Assert.IsTrue(await verificationProvider.ReleaseLockAsync(key, "verification"));
    }

    [TestMethod]
    public async Task AcquireLock_WithImmediateExpiry_DoesNotLeaveUnscheduledSessionLock()
    {
        using var owner = CreateProvider();
        using var contender = CreateProvider();
        string key = $"immediate-expiry-{Guid.NewGuid():N}";

        _ = await owner.AcquireLockAsync(key, "owner", TimeSpan.FromTicks(1));

        Assert.IsTrue(await AcquireEventuallyAsync(contender, key, "contender"));
        Assert.IsTrue(await contender.ReleaseLockAsync(key, "contender"));
    }

    [TestMethod]
    public async Task AcquireLock_WhenArmedTimerExpires_ReleasesPooledSession()
    {
        using var owner = CreateProvider();
        using var contender = CreateProvider();
        string key = $"timer-expiry-{Guid.NewGuid():N}";

        Assert.IsTrue(await owner.AcquireLockAsync(key, "owner", TimeSpan.FromMilliseconds(300)));
        Assert.IsTrue(await AcquireEventuallyAsync(contender, key, "contender"));
        Assert.IsTrue(await contender.ReleaseLockAsync(key, "contender"));
    }

    [TestMethod]
    public async Task ForceReleaseLock_ReleasesPooledSessionForIndependentProvider()
    {
        using var owner = CreateProvider();
        using var contender = CreateProvider();
        string key = $"force-release-{Guid.NewGuid():N}";

        Assert.IsTrue(await owner.AcquireLockAsync(key, "owner", TimeSpan.FromSeconds(10)));
        Assert.IsTrue(await owner.ForceReleaseLockAsync(key));
        Assert.IsTrue(await contender.AcquireLockAsync(key, "contender", TimeSpan.FromSeconds(10)));
        Assert.IsTrue(await contender.ReleaseLockAsync(key, "contender"));
    }

    [TestMethod]
    public async Task Dispose_ReleasesPooledSessionForIndependentProvider()
    {
        var owner = CreateProvider();
        using var contender = CreateProvider();
        string key = $"dispose-{Guid.NewGuid():N}";

        Assert.IsTrue(await owner.AcquireLockAsync(key, "owner", TimeSpan.FromSeconds(10)));
        owner.Dispose();

        Assert.IsTrue(await contender.AcquireLockAsync(key, "contender", TimeSpan.FromSeconds(10)));
        Assert.IsTrue(await contender.ReleaseLockAsync(key, "contender"));
    }

    [TestMethod]
    public async Task Dispose_WhenGateHeldOnSingleThreadContext_CompletesWithoutDeadlock()
    {
        var verificationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueVerification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var owner = new SqlServerLockProvider(
            _connectionString,
            NullLogger<SqlServerLockProvider>.Instance,
            TimeProvider.System,
            ownershipVerificationDelay: cancellationToken =>
            {
                verificationEntered.TrySetResult();
                return continueVerification.Task.WaitAsync(cancellationToken);
            });
        string key = $"dispose-context-{Guid.NewGuid():N}";
        Assert.IsTrue(await owner.AcquireLockAsync(key, "owner", TimeSpan.FromSeconds(10)));

        using var context = new DedicatedThreadSynchronizationContext();
        await context.Started;
        var extensionCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try
            {
                bool extended = await owner.ExtendLockAsync(
                    key,
                    "owner",
                    TimeSpan.FromSeconds(10));
                extensionCompleted.TrySetResult(extended);
            }
            catch (Exception ex)
            {
                extensionCompleted.TrySetException(ex);
            }
        }, null);

        await verificationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ =>
        {
            disposeStarted.TrySetResult();
            try
            {
                owner.Dispose();
                disposeCompleted.TrySetResult();
            }
            catch (Exception ex)
            {
                disposeCompleted.TrySetException(ex);
            }
        }, null);

        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        continueVerification.TrySetResult();

        await disposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(await extensionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await context.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task<bool> AcquireEventuallyAsync(
        SqlServerLockProvider provider,
        string key,
        string value)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                if (await provider.AcquireLockAsync(
                        key,
                        value,
                        TimeSpan.FromSeconds(10),
                        timeout.Token))
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }

        return false;
    }

    private static SqlServerLockProvider CreateProvider()
        => new(_connectionString, NullLogger<SqlServerLockProvider>.Instance);

    private sealed class DedicatedThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _work = new();
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;

        public DedicatedThreadSynchronizationContext()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = nameof(DedicatedThreadSynchronizationContext)
            };
            _thread.Start();
        }

        public Task Started => _started.Task;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _work.Add((callback, state));
        }

        public Task DrainAsync()
        {
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ => drained.TrySetResult(), null);
            return drained.Task;
        }

        public void Dispose()
        {
            _work.CompleteAdding();
            if (!_thread.Join(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The synchronization-context thread did not stop.");
            }

            _work.Dispose();
        }

        private void Run()
        {
            SetSynchronizationContext(this);
            _started.TrySetResult();
            foreach ((SendOrPostCallback callback, object? state) in _work.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
