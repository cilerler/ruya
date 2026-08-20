using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Ruya.Testing.Primitives.SampleTest;

[TestClass]
public static class AssemblySetup
{
	[AssemblyInitialize]
	public static void Init(TestContext context)
	{
		TestHost.Initialize((services, configuration) =>
		{
			services.AddScoped<IPingPongService, PingPongService>();
		});
	}

	[AssemblyCleanup]
	public static async Task Cleanup() => await TestHost.CleanupAsync();
}
