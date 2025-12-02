using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Tests.Common;

namespace Ruya.Services.CloudStorage.Local.Tests;

[TestClass]
#pragma warning disable CA1515 // Types can be made internal
public class ClientTest : CloudStorageTestBase
#pragma warning restore CA1515 // Types can be made internal
{
    protected override ICloudFileService GetClient()
    {
        var factory = ScopeServiceProvider.GetRequiredService<ICloudStorageFactory>();
        return factory.GetService(StorageServiceSettings.ProviderName);
    }

    protected override string GetBucketName() => "myBucket";

    [TestMethod]
	public async Task GetFileMetadataAsync_ShouldThrowFileNotFound_WhenFileDoesNotExistAsync()
    {
        var client = GetClient();
        var fileName = $"nonexistent_{Guid.NewGuid()}.txt";
        try
        {
            await client.GetFileMetadataAsync(GetBucketName(), fileName);
            Assert.Fail("Expected FileNotFoundException");
        }
        catch (System.IO.FileNotFoundException)
        {
            // Expected
        }
    }

	[TestMethod]
    public async Task UploadFileAsync_ShouldPreventPathTraversalAsync()
    {
        var client = GetClient();
        var bucketName = GetBucketName();
        var fileName = "traversal_test.txt";
		await File.WriteAllTextAsync(fileName, "dummy content");
		var localPath = Path.GetFullPath(fileName);

        // Try to write outside the bucket
        var remotePath = "../outside_bucket.txt";

        try
        {
            await client.UploadFileAsync(bucketName, localPath, remotePath);
            Assert.Fail("Expected ArgumentException");
        }
        catch (ArgumentException)
        {
            // Expected
        }
    }

	[TestMethod]
	public async Task DeleteFileAsync_ShouldCleanupEmptyDirectoriesAsync()
    {
        var client = GetClient();
        var bucketName = GetBucketName();
        var fileName = "cleanup_test.txt";
        await File.WriteAllTextAsync(fileName, "dummy content");
        var localPath = Path.GetFullPath(fileName);
        var remotePath = "nested/folder/structure/file.txt";

        // Upload file to create nested structure
        await client.UploadFileAsync(bucketName, localPath, remotePath);

        // Verify file exists
        var metadata = await client.GetFileMetadataAsync(bucketName, remotePath);
        Assert.IsNotNull(metadata);

        // Delete file
        await client.DeleteFileAsync(bucketName, remotePath);

        // Verify file is gone
        try
        {
            await client.GetFileMetadataAsync(bucketName, remotePath);
            Assert.Fail("Expected FileNotFoundException");
        }
        catch (System.IO.FileNotFoundException)
        {
            // Expected
        }

        // Verify directories are cleaned up
        var files = client.GetFileListAsync(bucketName, "nested");
        var count = 0;
        await foreach (var _ in files) count++;
        Assert.AreEqual(0, count, "Directory should be empty and thus not return any files");
    }
}
