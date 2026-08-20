using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Testing.Primitives;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Azure;
using System;
using System.Collections.Generic;
using System.IO;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using System.Threading.Tasks;

namespace Ruya.Services.CloudStorage.Azure.Tests;

[TestClass]
public static class AssemblySetup
{
    private static IContainer? _container;
    private static bool _isEmulator = true;
    private static readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);

    [AssemblyInitialize]
    public static void Init(TestContext context)
    {
        InitializeAsync(context).GetAwaiter().GetResult();
    }

    private static async Task InitializeAsync(TestContext context)
    {
        var testMode = Environment.GetEnvironmentVariable("TEST_MODE");
        _isEmulator = string.IsNullOrEmpty(testMode) || !testMode.Equals("REAL", StringComparison.OrdinalIgnoreCase);

        string connectionString;

        if (_isEmulator)
        {
            int port = Ruya.Services.CloudStorage.Tests.Common.TestUtils.GetAvailablePort();
            _container = new ContainerBuilder()
                .WithImage("mcr.microsoft.com/azure-storage/azurite")
                .WithPortBinding(port, 10000)
                .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--blobPort", "10000")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(10000).ForPath("/").ForStatusCode(System.Net.HttpStatusCode.BadRequest)))
                .Build();

            await _container.StartAsync();

            var host = _container.Hostname;
            if (host == "0.0.0.0" || host == "::0") host = "127.0.0.1";

            connectionString = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://{host}:{port}/devstoreaccount1;";
        }
        else
        {
            connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "UseDevelopmentStorage=true";
        }

        SetTestEnvironmentVariable("RUYA_TEST_CloudStorage__Azure__ConnectionStringKey", "AzureStorage");
        SetTestEnvironmentVariable("RUYA_TEST_CloudStorage__Azure__Container", "mybucket");
        SetTestEnvironmentVariable("RUYA_TEST_ConnectionStrings__AzureStorage", connectionString);

        TestHost.Initialize((services, configuration) =>
        {
            services.AddSingleton<System.Diagnostics.Metrics.IMeterFactory, Ruya.Services.CloudStorage.Tests.Common.StubMeterFactory>();
            services.AddSingleton<Ruya.Diagnostics.DistributedTracing.IDistributedTracing, Ruya.Services.CloudStorage.Tests.Common.StubDistributedTracing>();
            services.AddAzureStorageService();
        });
        
        if (!File.Exists("test_file.ignore.txt")) File.WriteAllText("test_file.ignore.txt", "dummy content");
    }

    [AssemblyCleanup]
    public static async Task Cleanup()
    {
        try
        {
            await TestHost.CleanupAsync();
            if (_container != null)
            {
                await _container.StopAsync();
            }
        }
        finally
        {
            RestoreTestEnvironment();
        }
    }

    private static void SetTestEnvironmentVariable(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    private static void RestoreTestEnvironment()
    {
        foreach ((string name, string? value) in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _originalEnvironment.Clear();
    }
}
