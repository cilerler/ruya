using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Testing.Primitives;

namespace Ruya.Extensions.Hosting.Unit.Tests;

[TestClass]
public static class AssemblySetup
{
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        TestHost.Initialize();
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        TestHost.Cleanup();
    }
}
