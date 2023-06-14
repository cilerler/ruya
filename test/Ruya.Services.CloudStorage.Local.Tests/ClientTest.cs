using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.Local.Tests;

[TestClass]
public class ClientTest
{
	private static TestContext _testContext;
	private static IServiceProvider _serviceProvider;
	private static ILogger _logger;

	[ClassInitialize]
	public static void ClassInitialize(TestContext testContext)
	{
		_testContext = testContext;

		IServiceCollection serviceCollection = new ServiceCollection();
		using (IEnumerator<ServiceDescriptor> sc = Initialize.ServiceCollection.GetEnumerator())
		{
			while (sc.MoveNext()) serviceCollection.Add(sc.Current);
		}

		serviceCollection.AddLocalStorageService(Initialize.ServiceProvider.GetRequiredService<IConfiguration>());

		_serviceProvider = serviceCollection.BuildServiceProvider();
		_logger = _serviceProvider.GetRequiredService<ILogger<ClientTest>>();
		Task.Delay(TimeSpan.FromSeconds(1)).Wait();
	}

	[TestCategory("Writers")]
	[Priority(1)]
	[DataTestMethod]
	[DataRow("myBucket", "", "test_file.ignore.txt")]
	[DataRow("myBucket", "Test", "test_file.ignore.txt")]
	public void UploadFileTest(string bucketName, string remoteLocation, string fileName)
	{
		string localPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
		string remotePath = $"{remoteLocation}/{fileName}".TrimStart(Path.AltDirectorySeparatorChar);

		ICloudFileService? client = _serviceProvider.GetService<ICloudFileService>();

		try
		{
			ICloudFileMetadata uploadedFile = client.UploadFile(localPath, remotePath, bucketName);
			bool uploadSuccess = uploadedFile.LastModified != null;
			Assert.IsTrue(uploadSuccess);
		}
		catch (ArgumentException ae) when (ae.Message.EndsWith("valid file extension"))
		{
			Assert.Fail(ae.Message);
		}
		catch (Exception ex)
		{
			Assert.Fail(ex.Message);
		}
	}

	[Priority(2)]
	[TestCategory("Readers")]
	[DataTestMethod]
	[DataRow("myBucket", "", "test_file.ignore.txt")]
	[DataRow("myBucket", "Test", "test_file.ignore.txt")]
	public void GetFileMetadataTest(string bucketName, string remoteLocation, string fileName)
	{
		string remotePath = $"{remoteLocation}/{fileName}".TrimStart(Path.AltDirectorySeparatorChar);

		ICloudFileService? client = _serviceProvider.GetService<ICloudFileService>();
		ICloudFileMetadata actual = null;

		try
		{
			actual = client.GetFileMetadata(remotePath, bucketName);
			bool retrieveSuccess = actual?.LastModified != null;
			if (!retrieveSuccess) throw new Exception("File or date is `null`, something is not correct");
		}
		catch (ArgumentException ae) when (ae.Message.StartsWith("Not found"))
		{
			Assert.Fail("File not found");
		}
		catch (Exception ex)
		{
			Assert.Fail(ex.Message);
		}

		bool actualDateExist = actual.LastModified != null;
		bool actualSizeExist = actual.Size > 0;
		string expectedSignedUrl = actual.SignedUrl.StartsWith(@"C:\")
			? actual.SignedUrl
			: string.Empty;

		string expectedBucket = bucketName;
		string expectedName = remotePath;

		Assert.AreEqual(expectedSignedUrl, actual.SignedUrl);
		Assert.AreEqual(expectedBucket, actual.Bucket);
		Assert.AreEqual(expectedName, actual.Name);
		Assert.IsTrue(actualDateExist);
		Assert.IsTrue(actualSizeExist);
	}

	[Priority(2)]
	[TestCategory("Readers")]
	[TestMethod]
	[DataRow("myBucket", "Test", "test_file.ignore.txt")]
	public void DownloadFileTest(string bucketName, string remoteLocation, string fileName)
	{
		string localPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
		string remotePath = $"{remoteLocation}/{fileName}".TrimStart(Path.AltDirectorySeparatorChar);

		var fileNameSuffix = $".{bucketName}";
		string targetFilePath = Path.GetDirectoryName(localPath) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(localPath) +
		                        fileNameSuffix + Path.GetExtension(localPath);
		if (File.Exists(targetFilePath)) File.Delete(targetFilePath);


		ICloudFileService? client = _serviceProvider.GetService<ICloudFileService>();
		using (FileStream downloadStream = File.OpenWrite(targetFilePath))
		{
			client.DownloadFile(remotePath, downloadStream, bucketName);
		}

		Assert.IsTrue(File.Exists(targetFilePath));

		bool fileHasData = new FileInfo(targetFilePath).Length > 0;
		File.Delete(targetFilePath);

		Assert.IsTrue(fileHasData);
	}
}
