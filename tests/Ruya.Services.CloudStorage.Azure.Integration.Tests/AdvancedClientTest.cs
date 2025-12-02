using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Azure;
using Ruya.Services.CloudStorage.Tests.Common;

namespace Ruya.Services.CloudStorage.Azure.Tests;

[TestClass]
public class AdvancedClientTest : CloudStorageTestBase
{
    protected override ICloudFileService GetClient()
    {
        var factory = ScopeServiceProvider.GetRequiredService<ICloudStorageFactory>();
        return factory.GetService(Setting.ProviderName);
    }

    protected override string GetBucketName() => "advanced-test-bucket";

    [TestMethod]
    public async Task ConcurrentUploads_ShouldSucceed()
    {
        var client = GetClient();
        var bucketName = GetBucketName();
        int fileCount = 10;
        var tasks = new List<Task>();
        var uploadedFiles = new ConcurrentBag<string>();

        for (int i = 0; i < fileCount; i++)
        {
            var fileName = $"concurrent_test_{i}.txt";
            var localPath = Path.GetFullPath(fileName);
            File.WriteAllText(localPath, $"Content for {fileName}");

            tasks.Add(Task.Run(async () =>
            {
                await client.UploadFileAsync(bucketName, localPath, fileName);
                uploadedFiles.Add(fileName);
            }));
        }

        await Task.WhenAll(tasks);

        Assert.AreEqual(fileCount, uploadedFiles.Count);

        // Verify all exist
        foreach (var file in uploadedFiles)
        {
            var metadata = await client.GetFileMetadataAsync(bucketName, file);
            Assert.IsNotNull(metadata);
        }
    }

    [TestMethod]
    public async Task UploadFile_WithCancellation_ShouldCancel()
    {
        var client = GetClient();
        var bucketName = GetBucketName();
        var fileName = "cancel_test.txt";
        var localPath = Path.GetFullPath(fileName);

        // Create a large file to ensure we have time to cancel
        var data = new byte[1024 * 1024 * 10]; // 10MB
        new Random().NextBytes(data);
        await File.WriteAllBytesAsync(localPath, data);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(10); // Cancel very quickly

        try
        {
            await client.UploadFileAsync(bucketName, localPath, fileName, cts.Token);
            Assert.Fail("Should have thrown OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            // It's possible it finished too fast or threw something else, but we expect cancellation.
            // If it's a RequestFailedException with cancellation status, that's also valid.
             if (ex is global::Azure.RequestFailedException rfe && rfe.Status == 0)
             {
                 // Azure SDK sometimes wraps cancellation in RequestFailedException with status 0
                 return;
             }
             throw;
        }
    }
}
