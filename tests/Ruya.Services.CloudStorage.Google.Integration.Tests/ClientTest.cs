using System;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Tests.Common;

namespace Ruya.Services.CloudStorage.Google.Tests;

[TestClass]
public class ClientTest : CloudStorageTestBase
{
    protected override ICloudFileService GetClient()
    {
        var factory = ScopeServiceProvider.GetRequiredService<ICloudStorageFactory>();
        return factory.GetService(StorageServiceSettings.ProviderName);
    }

    protected override string GetBucketName()
    {
        var emulatorHost = Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST");
        return string.IsNullOrEmpty(emulatorHost) ? "sp-data-dev" : "mybucket";
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("Emulator")]
    public async Task GetSignedUploadUrl_ShouldSucceed()
    {
        var client = GetClient();
        var bucketName = GetBucketName();
        var fileName = $"{GetRemoteLocation()}/{GetFileName()}";
        var contentType = "text/plain";

        // 1. Retrieve a url
        var uploadUrl = client.GetSignedUploadUrl(bucketName, fileName, contentType, 10);
        Assert.IsFalse(string.IsNullOrWhiteSpace(uploadUrl));

        // 2. Upload a file
        using var httpClient = new HttpClient();
        var content = new StringContent("test content", System.Text.Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var emulatorHost = Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST");
        if (!string.IsNullOrEmpty(emulatorHost) && uploadUrl.Contains("storage.googleapis.com"))
        {
             // HACK: Redirect to emulator for testing purposes because UrlSigner generates production URLs
             // and the Client doesn't expose a way to change the signer's base URI easily.
             // The emulator expects path style: http://host:port/bucket/object
             // But the signed URL is usually https://storage.googleapis.com/bucket/object
             // We need to replace the host and scheme.

             // emulatorHost is like "http://127.0.0.1:14443"
             uploadUrl = uploadUrl.Replace("https://storage.googleapis.com", emulatorHost);

             var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
             request.Content = content;
             // The signature was generated for storage.googleapis.com, so we must match the Host header
             // even though we are sending the request to the emulator.
             request.Headers.Host = "storage.googleapis.com";

             var response = await httpClient.SendAsync(request);
             Assert.IsTrue(response.IsSuccessStatusCode, $"Upload failed: {response.StatusCode}");
        }
        else
        {
             // Real service or already correct URL
             var response = await httpClient.PutAsync(uploadUrl, content);

             if (!response.IsSuccessStatusCode)
             {
                 var responseContent = await response.Content.ReadAsStringAsync();
                 Console.WriteLine($"Upload failed: {response.StatusCode} {responseContent}");
             }

             Assert.IsTrue(response.IsSuccessStatusCode, $"Upload failed: {response.StatusCode}");
        }

        // 3. Confirm the upload via getmetadata
        var metadata = await client.GetFileMetadataAsync(bucketName, fileName);
        Assert.IsNotNull(metadata);
        Assert.AreEqual(fileName, metadata.Name);
    }


    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetSignedUploadUrl_Expired_ShouldFail()
    {
        var client = GetClient();
        var bucketName = GetBucketName();
        var fileName = $"{GetRemoteLocation()}/expired_{GetFileName()}";
        var contentType = "text/plain";

        var emulatorHost = Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST");
        if (!string.IsNullOrEmpty(emulatorHost))
        {
            Assert.Inconclusive("Skipping expiration test on emulator as it does not enforce signed URL expiration.");
            return;
        }

        // 1. Retrieve a url with 1 minute expiration (minimum allowed by interface)
        var uploadUrl = client.GetSignedUploadUrl(bucketName, fileName, contentType, 1);
        Assert.IsFalse(string.IsNullOrWhiteSpace(uploadUrl));

        // 2. Wait for expiration (1 minute + buffer)
		Logger.LogInformation("Waiting 65 seconds for URL to expire...");
        await Task.Delay(TimeSpan.FromSeconds(65));

        // 3. Attempt upload
        using var httpClient = new HttpClient();
        var content = new StringContent("test content", System.Text.Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var response = await httpClient.PutAsync(uploadUrl, content);
        Assert.IsFalse(response.IsSuccessStatusCode, $"Upload should have failed, but got: {response.StatusCode}");
    }
}
