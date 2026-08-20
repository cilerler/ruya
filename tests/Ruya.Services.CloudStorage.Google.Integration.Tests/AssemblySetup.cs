using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Services.CloudStorage.Tests.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Ruya.Testing.Primitives;

namespace Ruya.Services.CloudStorage.Google.Tests;

[TestClass]
public static class AssemblySetup
{
	private const string _storageEmulatorHostEnvVar = "STORAGE_EMULATOR_HOST";
	private static IContainer? _container;

	[AssemblyInitialize]
	public static async Task AssemblyInitializeAsync(TestContext testContext)
	{
		TestHost.Initialize((services, configuration) =>
		{
            services.AddSingleton<IMeterFactory, StubMeterFactory>();
            services.AddSingleton<IDistributedTracing, StubDistributedTracing>();
			services.AddGoogleStorageService();
		});

#pragma warning disable CA1062 // Validate arguments of public methods
		await EmulatorHandlerAsync(testContext);
#pragma warning restore CA1062 // Validate arguments of public methods
	}

	[AssemblyCleanup]
	public static async Task AssemblyCleanupAsync(TestContext testContext)
	{
		await TestHost.CleanupAsync();

		Environment.SetEnvironmentVariable(_storageEmulatorHostEnvVar, null);
		if (_container != null)
#pragma warning disable CA1062 // Validate arguments of public methods
			await _container.StopAsync(testContext.CancellationToken);
#pragma warning restore CA1062 // Validate arguments of public methods
	}


	private static async Task EmulatorHandlerAsync(TestContext testContext)
	{
		var testMode = Environment.GetEnvironmentVariable("TEST_MODE");
		var isEmulator = string.IsNullOrEmpty(testMode) || !testMode.Equals("Integration", StringComparison.OrdinalIgnoreCase);
		if (!isEmulator)
		{
			Environment.SetEnvironmentVariable(_storageEmulatorHostEnvVar, null);
			return;
		}

		int hostPort = Ruya.Services.CloudStorage.Tests.Common.TestUtils.GetAvailablePort();
		const string hostAddress = "127.0.0.1";
		string serviceUrl = $"http://{hostAddress}:{hostPort}";
		_container = new ContainerBuilder()
			.WithImage("fsouza/fake-gcs-server")
			.WithPortBinding(hostPort, 4443)
			.WithCommand("-scheme", "http", "-external-url", $"http://{hostAddress}:{hostPort}", "-backend", "memory")
			.WithWaitStrategy(
				Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
					request => request
						.ForPort(4443)
						.ForPath("/storage/v1/b")
						.ForStatusCode(System.Net.HttpStatusCode.OK),
					waitStrategy => waitStrategy.WithTimeout(TimeSpan.FromSeconds(30))))
			.Build();
		await _container.StartAsync(testContext.CancellationToken);

		Environment.SetEnvironmentVariable(_storageEmulatorHostEnvVar, serviceUrl);

		using var httpClient = new HttpClient();
		var requestUri = new Uri($"{serviceUrl}/storage/v1/b?project=project-id");
		using var content = new StringContent("{\"name\": \"mybucket\"}", System.Text.Encoding.UTF8, "application/json");
		var response = await httpClient.PostAsync(requestUri, content, testContext.CancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			Console.WriteLine($"Bucket creation failed via API: {response.StatusCode} {await response.Content.ReadAsStringAsync(testContext.CancellationToken)}");
		}
	}
}
