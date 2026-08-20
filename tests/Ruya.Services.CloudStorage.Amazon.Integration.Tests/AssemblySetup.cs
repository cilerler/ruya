using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Testing.Primitives;

namespace Ruya.Services.CloudStorage.Amazon.Tests;

[TestClass]
public static class AssemblySetup
{
    private static IContainer? _container;
    private static bool _isEmulator = true;
    private static readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);

    [AssemblyInitialize]
    public static async Task AssemblyInit(TestContext testContext)
    {
        var testMode = Environment.GetEnvironmentVariable("TEST_MODE");
        _isEmulator = string.IsNullOrEmpty(testMode) || !testMode.Equals("REAL", StringComparison.OrdinalIgnoreCase);

        string serviceUrl = null;
        string accessKey = "test";
        string secretKey = "test";
        string region = "us-east-1";

        if (_isEmulator)
        {
            var port = Ruya.Services.CloudStorage.Tests.Common.TestUtils.GetAvailablePort();
            _container = new ContainerBuilder()
                .WithImage("localstack/localstack")
                .WithPortBinding(port, 4566)
                .WithEnvironment("SERVICES", "s3")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(4566).ForPath("/_localstack/health").ForStatusCode(System.Net.HttpStatusCode.OK)))
                .Build();

            await _container.StartAsync();

            var host = _container.Hostname;
            if (host == "0.0.0.0" || host == "::0") host = "127.0.0.1";

            serviceUrl = $"http://{host}:{port}";
        }
        else
        {
            accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "test";
            secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "test";
            region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
        }

        SetTestEnvironmentVariable("RUYA_TEST_CloudStorage__Amazon__AccessKey", accessKey);
        SetTestEnvironmentVariable("RUYA_TEST_CloudStorage__Amazon__SecretKey", secretKey);
        SetTestEnvironmentVariable("RUYA_TEST_CloudStorage__Amazon__Region", region);

        if (!string.IsNullOrEmpty(serviceUrl))
        {
            SetTestEnvironmentVariable("RUYA_TEST_CloudStorage__Amazon__ServiceUrl", serviceUrl);
        }

        TestHost.Initialize((services, configuration) =>
        {
            services.AddAmazonStorageService();
        });

        // Create bucket manually if using emulator or if needed
        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = global::Amazon.RegionEndpoint.GetBySystemName(region),
            ForcePathStyle = true 
        };
        
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            s3Config.ServiceURL = serviceUrl;
        }

        using var s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);
        try
        {
            await s3Client.PutBucketAsync("mybucket");
        }
        catch (Exception ex) {
             Console.WriteLine("Bucket creation warning: " + ex.Message);
        }
    }



    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
    {
        try
        {
            await TestHost.CleanupAsync();
            if (_container != null) await _container.StopAsync();
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
