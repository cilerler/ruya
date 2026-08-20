using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;

using Ruya.Testing.Primitives;

namespace Ruya.Services.CloudStorage.Tests.Common
{
    public abstract class CloudStorageTestBase : TestBase<CloudStorageTestBase>
    {
        protected abstract ICloudFileService GetClient();
        protected abstract string GetBucketName();
        protected virtual string GetFileName() => "test_file.ignore.txt";
        protected virtual string GetRemoteLocation() => "Test";

        [TestInitialize]
        public virtual void Setup()
        {
            var fileName = GetFileName();
            if (!File.Exists(fileName))
            {
                File.WriteAllText(fileName, "This is a test file " + Guid.NewGuid());
            }
        }

        [TestMethod]
        public async Task UploadFile_ShouldSucceed()
        {
            var client = GetClient();
            var bucketName = GetBucketName();
            var fileName = GetFileName();
            var localPath = Path.GetFullPath(fileName);
            var remotePath = $"{GetRemoteLocation()}/{fileName}";

            var result = await client.UploadFileAsync(bucketName, localPath, remotePath);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.LastModified);
            Assert.AreEqual(bucketName, result.Bucket);
        }

        [TestMethod]
        public async Task GetFileMetadata_ShouldReturnCorrectData()
        {
            var client = GetClient();
            var bucketName = GetBucketName();
            var fileName = GetFileName();
            var localPath = Path.GetFullPath(fileName);
            var remotePath = $"{GetRemoteLocation()}/{fileName}";

            // Ensure file exists
            await client.UploadFileAsync(bucketName, localPath, remotePath);

            var metadata = await client.GetFileMetadataAsync(bucketName, remotePath);

            Assert.IsNotNull(metadata);
            Assert.IsNotNull(metadata.LastModified);
            Assert.AreEqual(bucketName, metadata.Bucket);
            Assert.IsNotNull(metadata.SignedUrl);
        }

        [TestMethod]
        public async Task DownloadFile_ShouldSucceed()
        {
            var client = GetClient();
            var bucketName = GetBucketName();
            var fileName = GetFileName();
            var localPath = Path.GetFullPath(fileName);
            var remotePath = $"{GetRemoteLocation()}/{fileName}";

            // Ensure file exists
            await client.UploadFileAsync(bucketName, localPath, remotePath);

            using var ms = new MemoryStream();
            await client.DownloadFileAsync(bucketName, remotePath, ms);

            Assert.IsTrue(ms.Length > 0);
        }

        [TestMethod]
        public async Task GetFileList_ShouldReturnFiles()
        {
            var client = GetClient();
            var bucketName = GetBucketName();
            var fileName = GetFileName();
            var localPath = Path.GetFullPath(fileName);
            var remotePath = $"{GetRemoteLocation()}/{fileName}";

            // Ensure file exists
            await client.UploadFileAsync(bucketName, localPath, remotePath);

            var files = new List<CloudFileMetadata>();
            await foreach (var file in client.GetFileListAsync(bucketName))
            {
                files.Add(file);
            }

            Assert.IsTrue(files.Count > 0);
            Assert.IsTrue(files.Any(f => f.Name.Contains(fileName)));
        }

        [TestMethod]
        public async Task DeleteFile_ShouldSucceed()
        {
             var client = GetClient();
            var bucketName = GetBucketName();
            var fileName = GetFileName();
            var localPath = Path.GetFullPath(fileName);
            var remotePath = $"{GetRemoteLocation()}/delete_test_{fileName}";

            // Ensure file exists
            await client.UploadFileAsync(bucketName, localPath, remotePath, TestContext.CancellationToken);

            // Verify it exists
            var metadata = await client.GetFileMetadataAsync(bucketName, remotePath, TestContext.CancellationToken);
            Assert.IsNotNull(metadata);

            // Delete
            await client.DeleteFileAsync(bucketName, remotePath, TestContext.CancellationToken);

            // Verify it's gone
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                client.GetFileMetadataAsync(bucketName, remotePath, TestContext.CancellationToken));
        }
    }
}
