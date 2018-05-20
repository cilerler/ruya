using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using HeyRed.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Ruya.Services.CloudStorage.Abstractions;
using Object = Google.Apis.Storage.v1.Data.Object;

namespace Ruya.Services.CloudStorage.Google
{
	public class Client : ICloudFileService
	{
		private readonly ILogger _logger;
		private readonly Setting _options;

		private readonly StorageClient _storageClient;
		private readonly UrlSigner _urlSigner;
		private string _bucketName;

	    // ReSharper disable once SuggestBaseTypeForParameter
		public Client(ILogger<Client> logger, IOptions<Setting> options)
		{
			_logger = logger;
			_options = options.Value;

			GoogleCredential credentials = GetGoogleCredentials();
			var serviceAccountCredential = credentials.UnderlyingCredential as ServiceAccountCredential;
			_urlSigner = UrlSigner.FromServiceAccountCredential(serviceAccountCredential);
			_storageClient = StorageClient.Create(credentials);
		}

		private GoogleCredential GetGoogleCredentials()
		{
			GoogleCredential output;
			try
			{
				output = GoogleCredential.FromJson(_options.Credential);
			}
			catch (InvalidOperationException ioe) when (ioe.InnerException is JsonSerializationException)
			{
				throw;
			}
			// ReSharper disable once RedundantCatchClause
			catch (Exception)
			{
				throw;
			}
			return output;
		}

		public void SetBucket(string input)
		{
			_bucketName = input;
		}

		private void EnsureBucketExist(string bucketName)
		{
			bool newBucketNameExist = !string.IsNullOrWhiteSpace(bucketName);
			if (newBucketNameExist)
			{
				SetBucket(bucketName);
				return;
			}

			bool bucketNameExist = !string.IsNullOrWhiteSpace(_bucketName);
		    if (bucketNameExist)
		    {
		        return;
		    }

		    _logger.LogError("No bucket name set!");
		    throw new ArgumentNullException(nameof(_bucketName));
		}

		public ICloudFileMetadata GetFileMetadata(string fileName, string bucketName = "")
		{
			EnsureBucketExist(bucketName);
			try
			{
				Object file = _storageClient.GetObject(_bucketName, fileName);

				var output = new CloudFileMetadata
				             {
					             Bucket = file.Bucket,
					             Size = file.Size,
					             Name = file.Name,
					             LastModified = file.Updated,
                                 ContentType = file.ContentType,
					             SignedUrl = _urlSigner.Sign(file.Bucket, file.Name, new TimeSpan(1, 0, 0), HttpMethod.Get)
				             };
				return output;
			}
			catch (GoogleApiException gex)
			{
				if (gex.HttpStatusCode == HttpStatusCode.NotFound)
				{
					_logger.LogError("Not Found - {fileName} in bucket {bucketName}", fileName,_bucketName);
					//! References looks for starting part of the following message to identify file not found error
					throw new ArgumentException($"Not Found - {fileName} in bucket {_bucketName}", gex);
				}
				_logger.LogError(-1, gex, gex.Error.Message);
				throw;
			}
		}

		public ICloudFileMetadata UploadFile(string sourcePath, string targetPath, string bucketName = "") //, bool addTimeStampToFileName)
		{
			string contentType = null;
			try
			{
				contentType = MimeTypesMap.GetMimeType(sourcePath);
			}
			catch (Exception e)
			{
				_logger.LogWarning(e, "There is an error occured while trying to retrieve MimeType");
			}
			using (FileStream fileStream = File.OpenRead(sourcePath))
			{
				return string.IsNullOrWhiteSpace(contentType)
					       ? UploadStream(fileStream, targetPath, bucketName: bucketName)
					       : UploadStream(fileStream, targetPath, contentType, bucketName);
			}
		}

		public ICloudFileMetadata UploadStream(Stream source, string targetPath, string contentType = "application/octet-stream", string bucketName = "")
		{
			EnsureBucketExist(bucketName);

			//bool fileNameExistInTargetFolder = Path.GetExtension(targetPath) != string.Empty;
			//if (!fileNameExistInTargetFolder)
			//{
			//	throw new ArgumentException($"'{nameof(targetPath)}' does not have a valid file extension");
			//}

			string fileName = Path.GetFileName(targetPath);
			string directoryName = Path.GetDirectoryName(targetPath);
			string correctedDirectoryName = directoryName.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			                                             .Trim(Path.AltDirectorySeparatorChar);
			string destinationFileName = (correctedDirectoryName + Path.AltDirectorySeparatorChar + fileName).TrimStart(Path.AltDirectorySeparatorChar);

			var progress = new Progress<IUploadProgress>(p => _logger.LogTrace($"destination gs://{{bucket}}/{{destinationFileName}}, bytes: {p.BytesSent}, status: {p.Status}", _bucketName,destinationFileName));

			source.Seek(0, SeekOrigin.Begin);

			Object upload;
			try
			{
				upload = _storageClient.UploadObject(_bucketName, destinationFileName, contentType, source, progress: progress);
			}
			catch (GoogleApiException gex)
			{
				_logger.LogError(-1, gex, gex.Error.Message);
				throw;
			}

			var output = new CloudFileMetadata
			             {
				             Bucket = _bucketName,
				             Size = upload.Size,
				             LastModified = upload.Updated,
                             ContentType = upload.ContentType,
				             Name = destinationFileName
			             };
			return output;
		}

		public void DownloadFile(string fileName, Stream destinationStream, string bucketName = "")
		{
			EnsureBucketExist(bucketName);

			try
			{
				_storageClient.DownloadObject(_bucketName, fileName, destinationStream);
			}
			catch (GoogleApiException gex)
			{
				_logger.LogError(-1, gex, gex.Error.Message);
				throw;
			}
			destinationStream.Seek(0, SeekOrigin.Begin);
		}

        public void DeleteFile(string fileName, string bucketName = "")
        {
            EnsureBucketExist(bucketName);

            try
            {
                _storageClient.DeleteObject(_bucketName, fileName);
            }
            catch (GoogleApiException gex)
            {
                _logger.LogError(-1, gex, gex.Error.Message);
                throw;
            }
        }

        public void CopyFile(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName)
        {
            EnsureBucketExist(sourceBucketName);
            EnsureBucketExist(destinationBucketName);
            try
            {
                _storageClient.CopyObject(sourceBucketName, sourceFileName, destinationBucketName, destinationFileName);
            }
            catch (GoogleApiException gex)
            {
                _logger.LogError(-1, gex, gex.Error.Message);
                throw;
            }
        }

        public List<ICloudFileMetadata> GetFileList(string prefix = null, string bucketName = "")
        {
            EnsureBucketExist(bucketName);

            var output = new List<ICloudFileMetadata>();
            try
            {
                var files = _storageClient.ListObjects(_bucketName, prefix);
                foreach (var file in files)
                {
                    output.Add(new CloudFileMetadata
                    {
                        Bucket = file.Bucket,
                        Size = file.Size,
                        Name = file.Name,
                        LastModified = file.Updated,
                        ContentType = file.ContentType
                    });
                }
            }
            catch (GoogleApiException gex)
            {
                _logger.LogError(-1, gex, gex.Error.Message);
                throw;
            }

            return output;
        }

	}
}
