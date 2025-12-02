using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Services.CloudStorage.Tests.Common;
using Ruya.Testing.Primitives;

namespace Ruya.Services.CloudStorage.Local.Tests;

[TestClass]
public static class AssemblySetup
{
    [AssemblyInitialize]
    public static void Init(TestContext context)
    {
        TestHost.Initialize((services, configuration) =>
        {
            services.AddSingleton<IMeterFactory, StubMeterFactory>();
            services.AddSingleton<IDistributedTracing, StubDistributedTracing>();
            services.AddLocalStorageService();
        });
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        TestHost.Cleanup();
    }
}
