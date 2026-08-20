using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Services.CloudStorage.Tests.Common;
using Ruya.Testing.Primitives;

namespace Ruya.Services.CloudStorage.Local.Tests;

[TestClass]
public static class AssemblySetup
{
    private static readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        $"Ruya.CloudStorage.Local.Tests.{System.Guid.NewGuid():N}");

    [AssemblyInitialize]
    public static void Init(TestContext context)
    {
        TestHost.Initialize((services, configuration) =>
        {
            services.AddSingleton<IMeterFactory, StubMeterFactory>();
            services.AddSingleton<IDistributedTracing, StubDistributedTracing>();
            services.AddLocalStorageService();
            services.PostConfigure<StorageServiceSettings>(settings => settings.Path = _storageRoot);
        });
    }

    [AssemblyCleanup]
    public static async Task Cleanup()
    {
        await TestHost.CleanupAsync();
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
