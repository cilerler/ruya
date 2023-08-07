using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

using HeyRed.Mime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.Azure;

public class Client : ICloudFileService
{
	private readonly ILogger _logger;
	private readonly Setting _options;

	private readonly BlobContainerClient _storageClient;

	// ReSharper disable once SuggestBaseTypeForParameter
	public Client(IConfiguration configuration, ILogger<Client> logger, IOptions<Setting> options)
	{
		_logger = logger;
		_options = options.Value;
		var connectionString = configuration.GetConnectionString(_options.ConnectionStringKey) ?? throw new ArgumentNullException(nameof(_options.ConnectionStringKey));
		_storageClient = new BlobContainerClient(connectionString, _options.Container);
		_storageClient.CreateIfNotExists();
	}

	public void SetBucket(string input)
	{
		throw new NotSupportedException();
	}

	public ICloudFileMetadata GetFileMetadata(string fileName, string bucketName = "")
	{
		EnsureContainerExist(bucketName);
		try
		{
			BlobClient blobClient = _storageClient.GetBlobClient(fileName);
			BlobProperties properties = blobClient.GetProperties();

			var output = new CloudFileMetadata
			{
				Bucket = blobClient.BlobContainerName,
				Size = (ulong?)properties.ContentLength,
				Name = fileName,
				LastModified = properties.LastModified.UtcDateTime,
				ContentType = properties.ContentType,
				SignedUrl = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1)).ToString()
			};
			return output;
		}
		catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
		{
			_logger.Log(LogLevel.Warning, "Not Found - {fileName} in container {containerName}", fileName, _options.Container);
			throw new ArgumentException($"Not Found - {fileName} in container {_options.Container}", ex);
		}
		catch (RequestFailedException ex)
		{
			_logger.Log(LogLevel.Error, ex, ex.Message);
			throw;
		}
	}

	public ICloudFileMetadata UploadFile(string sourcePath, string targetPath, string bucketName = "")
	{
		string contentType = null;
		try
		{
			contentType = MimeTypesMap.GetMimeType(sourcePath);
		}
		catch (Exception e)
		{
			_logger.Log(LogLevel.Warning, e, "An error occured while trying to retrieve MimeType");
		}

		BlobClient blob = _storageClient.GetBlobClient(targetPath);
		using FileStream fileStream = File.OpenRead(sourcePath);
		return UploadStream(fileStream, targetPath, bucketName: bucketName);
	}

	public ICloudFileMetadata UploadStream(Stream source, string targetPath, string contentType = "application/octet-stream", string bucketName = "")
	{
		EnsureContainerExist(bucketName);

		string fileName = Path.GetFileName(targetPath);
		string directoryName = Path.GetDirectoryName(targetPath);
		string correctedDirectoryName = directoryName.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Trim(Path.AltDirectorySeparatorChar);
		string destinationFileName = (correctedDirectoryName + Path.AltDirectorySeparatorChar + fileName).TrimStart(Path.AltDirectorySeparatorChar);

		var progress = new Progress<long>(p => _logger.LogTrace("destination as://{container}/{destinationFileName}, progress: {progress}", _options.Container, destinationFileName, p));
		BlobClient blobClient = _storageClient.GetBlobClient(targetPath);
		source.Seek(0, SeekOrigin.Begin);
		BlobContentInfo upload;
		try
		{
			upload = blobClient.Upload(source, new BlobUploadOptions { ProgressHandler = progress }).Value;
		}
		catch (Exception e)
		{
			_logger.Log(LogLevel.Error, e, "Encountered an error while uploading file stream. {ContainerName} {FileName}", _options.Container, fileName);
			throw;
		}

		BlobProperties properties = blobClient.GetProperties().Value;
		var output = new CloudFileMetadata
		{
			Bucket = blobClient.BlobContainerName,
			Size = (ulong?)properties.ContentLength,
			Name = destinationFileName,
			LastModified = properties.LastModified.UtcDateTime,
			ContentType = properties.ContentType,
		};
		return output;
	}

	public void DownloadFile(string fileName, Stream destinationStream, string bucketName = "")
	{
		EnsureContainerExist(bucketName);

		try
		{
			_storageClient.GetBlobClient(fileName).DownloadTo(destinationStream);
		}
		catch (Exception e)
		{
			_logger.Log(LogLevel.Error, e, "Encountered an error while dowloading file. {ContainerName} {FileName}", _options.Container, fileName);
			throw;
		}

		destinationStream.Seek(0, SeekOrigin.Begin);
	}

	public void DeleteFile(string fileName, string bucketName = "")
	{
		EnsureContainerExist(bucketName);

		try
		{
			_storageClient.GetBlobClient(fileName).DeleteIfExists();
		}
		catch (Exception e)
		{
			_logger.Log(LogLevel.Error, e, "Encountered an error while deleting file. {ContainerName} {FileName}", _options.Container, fileName);
			throw;
		}
	}

	public void CopyFile(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName)
	{
		throw new NotImplementedException();
	}

	public List<ICloudFileMetadata> GetFileList(string prefix = null, string bucketName = "")
	{
		EnsureContainerExist(bucketName);

		var output = new List<ICloudFileMetadata>();
		try
		{
			var blobItems = _storageClient.GetBlobs(prefix: prefix);
			foreach (var blobItem in blobItems)
			{
				output.Add(new CloudFileMetadata
				{
					Bucket = _options.Container,
					Size = (ulong?)blobItem.Properties.ContentLength,
					Name = blobItem.Name,
					LastModified = blobItem.Properties.LastModified.Value.UtcDateTime,
					ContentType = blobItem.Properties.ContentType
				});
			}
		}
		catch (Exception e)
		{
			_logger.Log(LogLevel.Error, e, "Encountered an error while getting file list. {Prefix} {ContainerName}", prefix, _options.Container);
			throw;
		}

		return output;
	}

	private static void EnsureContainerExist(string containerName)
	{
		if (!string.IsNullOrWhiteSpace(containerName))
		{
			throw new NotSupportedException();
		}
	}

	public string GetSignedUploadUrl(string filename, string contentType, string bucketName, int expirationMinutes = 60)
	{
		throw new NotImplementedException();
	}
}
