using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Testing.Primitives.Unit.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AsyncTestBaseCleanupTests : TestBase<AsyncTestBaseCleanupTests>
{
    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        TestHost.Cleanup();
        TestHost.Initialize((services, _) => services.AddScoped<AsyncScopedProbe>());
    }

    [ClassCleanup]
    public static async Task Cleanup() => await TestHost.CleanupAsync();

    [TestMethod]
    public async Task BaseTestCleanupAsync_AsyncOnlyScopedService_DisposesService()
    {
        var probe = ScopeServiceProvider.GetRequiredService<AsyncScopedProbe>();

        await BaseTestCleanupAsync();

        Assert.IsTrue(probe.IsDisposed);
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Resolved from the TestBase-owned dependency-injection scope.")]
    private sealed class AsyncScopedProbe : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
