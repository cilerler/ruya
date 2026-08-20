using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Testing.Primitives.Unit.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TestHostTests
{
	[TestInitialize]
	public void TestInitialize() => TestHost.Cleanup();

	[TestCleanup]
	public void TestCleanup() => TestHost.Cleanup();

	[TestMethod]
	public void Initialize_DefaultConfiguration_UsesCanonicalTestServerConnectionString()
	{
		TestHost.Initialize();

		var configuration = TestHost.RootServiceProvider!.GetRequiredService<IConfiguration>();

		Assert.AreEqual("http://test.local:80", configuration["ConnectionStrings:TestServer"]);
		Assert.IsNull(configuration["ConnectionStrings::TestServer"]);
	}

	[TestMethod]
	public void Initialize_CallerWorkingDirectoryDiffers_LoadsSettingsBesideTestAssembly()
	{
		var originalDirectory = Directory.GetCurrentDirectory();
		var alternateDirectory = Path.Combine(Path.GetTempPath(), $"ruya-testing-primitives-{Guid.NewGuid():N}");
		Directory.CreateDirectory(alternateDirectory);

		try
		{
			Directory.SetCurrentDirectory(alternateDirectory);
			TestHost.Initialize();

			var configuration = TestHost.RootServiceProvider!.GetRequiredService<IConfiguration>();
			Assert.AreEqual("assembly-directory", configuration["FileBacked:Source"]);
		}
		finally
		{
			TestHost.Cleanup();
			Directory.SetCurrentDirectory(originalDirectory);
			Directory.Delete(alternateDirectory);
		}
	}

	[TestMethod]
	public void Initialize_AmbientAndPrefixedVariablesPresent_LoadsOnlyPrefixedEnvironmentVariables()
	{
		const string ambientName = "Isolation__AmbientValue";
		const string prefixedName = "RUYA_TEST_Isolation__ScopedValue";
		var originalAmbient = Environment.GetEnvironmentVariable(ambientName);
		var originalPrefixed = Environment.GetEnvironmentVariable(prefixedName);
		try
		{
			Environment.SetEnvironmentVariable(ambientName, "must-not-load");
			Environment.SetEnvironmentVariable(prefixedName, "expected");

			TestHost.Initialize();
			var configuration = TestHost.RootServiceProvider!.GetRequiredService<IConfiguration>();

			Assert.IsNull(configuration["Isolation:AmbientValue"]);
			Assert.AreEqual("expected", configuration["Isolation:ScopedValue"]);
		}
		finally
		{
			Environment.SetEnvironmentVariable(ambientName, originalAmbient);
			Environment.SetEnvironmentVariable(prefixedName, originalPrefixed);
		}
	}

	[TestMethod]
	public void Initialize_CustomPrefixProvided_LoadsOnlyCustomPrefix()
	{
		const string customName = "CUSTOM_RUYA_Isolation__Value";
		var original = Environment.GetEnvironmentVariable(customName);
		try
		{
			Environment.SetEnvironmentVariable(customName, "custom");

			TestHost.Initialize(customConfig: null, environmentVariablePrefix: "CUSTOM_RUYA_");
			var configuration = TestHost.RootServiceProvider!.GetRequiredService<IConfiguration>();

			Assert.AreEqual("custom", configuration["Isolation:Value"]);
		}
		finally
		{
			Environment.SetEnvironmentVariable(customName, original);
		}
	}

	[TestMethod]
	public void Initialize_AlreadyInitialized_ThrowsInvalidOperationException()
	{
		TestHost.Initialize();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => TestHost.Initialize());

		StringAssert.Contains(exception.Message, "already initialized", StringComparison.Ordinal);
	}

	[TestMethod]
	public void Initialize_SingletonConsumesScopedService_ThrowsAggregateException()
	{
		Assert.ThrowsExactly<AggregateException>(() => TestHost.Initialize((services, _) =>
		{
			services.AddScoped<ScopedDependency>();
			services.AddSingleton<InvalidSingleton>();
		}));

		Assert.IsNull(TestHost.RootServiceProvider);
	}

	[TestMethod]
	public void Cleanup_InitializedProvider_DisposesProviderAndClearsGlobalReference()
	{
		TestHost.Initialize((services, _) => services.AddSingleton<DisposableProbe>());
		var probe = TestHost.RootServiceProvider!.GetRequiredService<DisposableProbe>();

		TestHost.Cleanup();

		Assert.IsTrue(probe.IsDisposed);
		Assert.IsNull(TestHost.RootServiceProvider);
	}

	[TestMethod]
	public async Task CleanupAsync_AsyncOnlySingleton_DisposesProviderAndClearsGlobalReference()
	{
		TestHost.Initialize((services, _) => services.AddSingleton<AsyncDisposableProbe>());
		var probe = TestHost.RootServiceProvider!.GetRequiredService<AsyncDisposableProbe>();

		await TestHost.CleanupAsync();

		Assert.IsTrue(probe.IsDisposed);
		Assert.IsNull(TestHost.RootServiceProvider);
	}

	[TestMethod]
	public void Initialize_EmptyEnvironmentPrefix_ThrowsArgumentException()
	{
		Assert.ThrowsExactly<ArgumentException>(
			() => TestHost.Initialize(customConfig: null, environmentVariablePrefix: " "));
	}

	[SuppressMessage("Performance", "CA1812", Justification = "Resolved by dependency injection during graph validation.")]
	private sealed class ScopedDependency
	{
		public Guid InstanceId { get; } = Guid.NewGuid();
	}

	[SuppressMessage("Performance", "CA1812", Justification = "Resolved by dependency injection during graph validation.")]
	private sealed class InvalidSingleton(ScopedDependency dependency)
	{
		public ScopedDependency Dependency { get; } = dependency;
	}

	[SuppressMessage("Performance", "CA1812", Justification = "Resolved by dependency injection during provider-disposal verification.")]
	private sealed class DisposableProbe : IDisposable
	{
		public bool IsDisposed { get; private set; }

		public void Dispose() => IsDisposed = true;
	}

	[SuppressMessage("Performance", "CA1812", Justification = "Resolved by dependency injection during async provider-disposal verification.")]
	private sealed class AsyncDisposableProbe : IAsyncDisposable
	{
		public bool IsDisposed { get; private set; }

		public ValueTask DisposeAsync()
		{
			IsDisposed = true;
			return ValueTask.CompletedTask;
		}
	}
}
