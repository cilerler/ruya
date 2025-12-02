using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Testing.Primitives;

namespace Ruya.Services.CloudStorage.Amazon.Tests;

[TestClass]
public static class AssemblySetup
{
    private static IContainer? _container;
    private static bool _isEmulator = true;

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

        Environment.SetEnvironmentVariable("CloudStorage:Amazon:AccessKey", accessKey);
        Environment.SetEnvironmentVariable("CloudStorage:Amazon:SecretKey", secretKey);
        Environment.SetEnvironmentVariable("CloudStorage:Amazon:Region", region);

        if (!string.IsNullOrEmpty(serviceUrl))
        {
            Environment.SetEnvironmentVariable("CloudStorage:Amazon:ServiceUrl", serviceUrl);
        }

        TestHost.Initialize((services, configuration) =>
        {
            services.AddAmazonStorageService(configuration);
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

        var s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);
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
        TestHost.Cleanup();
        if (_container != null) await _container.StopAsync();
    }
}
