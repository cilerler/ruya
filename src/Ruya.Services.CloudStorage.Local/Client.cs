using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using HeyRed.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Primitives;

namespace Ruya.Services.CloudStorage.Local
{
	public class Client : ICloudFileService
	{
		private readonly ILogger _logger;
		private readonly Setting _options;
		private string _bucketName;

		public Client(ILogger<Client> logger, IOptions<Setting> options)
		{
			_logger = logger;
			_options = options.Value;
	    }

	    private string RootPath => Path.Combine(_options.Path, _bucketName);
	    private string GetFileName(string fullName) => fullName.Replace(RootPath, string.Empty).TrimStart(Path.DirectorySeparatorChar);

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
			    string filePath = Path.Combine(RootPath
                                             , fileName);
                var fileInfo = new FileInfo(filePath);                
				var output = new CloudFileMetadata
				             {
					             Bucket = _bucketName,
					             Size = (ulong)fileInfo.Length,
					             Name = GetFileName(fileInfo.FullName),
					             LastModified = fileInfo.LastWriteTimeUtc,
                                 ContentType = MimeTypesMap.GetMimeType(fileInfo.Name),
                                 SignedUrl = fileInfo.FullName
				             };
				return output;
			}
			catch (FileNotFoundException fnfe)
			{
			    _logger.LogError(fnfe, $"{nameof(GetFileMetadata)} : File Not Found - {{fileName}} in bucket {{bucketName}}", fileName, _bucketName);
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

            string destinationPath = Path.Combine(_options.Path
                                                , _bucketName
                                                , targetPath);
            
            //var progress = new Progress<IUploadProgress>(p => _logger.LogTrace($"destination gs://{{bucket}}/{{destinationFileName}}, bytes: {p.BytesSent}, status: {p.Status}", _bucketName, destinationFileName));

            source.Seek(0, SeekOrigin.Begin);

            try
            {
                if (!Directory.Exists(RootPath))
                {
                    throw new DirectoryNotFoundException($"{RootPath} does not exists.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                using (FileStream fileStream = File.Create(destinationPath))
                {
                    source.CopyTo(fileStream);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                throw;
            }

            var fileInfo = new FileInfo(destinationPath);
            var output = new CloudFileMetadata
                         {
                             Bucket = _bucketName
                           , Size = (ulong)fileInfo.Length
                           , Name = GetFileName(fileInfo.FullName)
                           , LastModified = fileInfo.LastWriteTimeUtc
                           , ContentType = MimeTypesMap.GetMimeType(fileInfo.Name)
                         };
            return output;
        }

	    public void DownloadFile(string fileName, Stream destinationStream, string bucketName = "")
	    {
	        EnsureBucketExist(bucketName);

	        try
	        {
	            string filePath = Path.Combine(RootPath, fileName);
                using (FileStream fileStream = File.OpenRead(filePath))
	            {
	                fileStream.CopyTo(destinationStream);
	            }
            }
	        catch (Exception e)
	        {
	            _logger.LogError(e, e.Message);
	            throw;
	        }
	        destinationStream.Seek(0, SeekOrigin.Begin);
	    }

        private void EnsureEmptyDirectoriesDeleted(string filePath)
	    {
	        string directoryPath = Path.GetDirectoryName(filePath);
	        bool entriesExist;
	        do
	        {
	            var entries = Directory.EnumerateFileSystemEntries(directoryPath)
	                                   .ToList();
	            entries.Remove(directoryPath);
	            entriesExist = entries.Any();
	            if (entriesExist)
	            {
	                continue;
	            }

	            try
	            {
	                _logger.LogInformation("Folder {folder} deleted."
	                                     , directoryPath);
	                Directory.Delete(directoryPath);
	            }
	            catch (Exception e)
	            {
	                _logger.LogError(e
	                               , "There is an error occured while deleting a folder {folder}"
	                               , directoryPath);
	                entriesExist = true;
	            }

	            int index = directoryPath.LastIndexOf(Path.DirectorySeparatorChar);
	            bool pathExists = index != -1;
	            if (pathExists)
	            {
	                directoryPath = directoryPath.Substring(0
	                                                      , index);
	            }
	        }
	        while (!entriesExist
	            && !directoryPath.Equals(RootPath));
	    }

	    public void DeleteFile(string fileName, string bucketName = "")
        {
            EnsureBucketExist(bucketName);

            try
            {
                string filePath = Path.Combine(RootPath, fileName);
                File.Delete(filePath);
                EnsureEmptyDirectoriesDeleted(filePath);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning($"{nameof(DeleteFile)} : File Not Found - {{fileName}} in bucket {{bucketName}}", fileName, _bucketName);
                throw;
            }
        }

        public void CopyFile(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName)
        {
            EnsureBucketExist(sourceBucketName);
            EnsureBucketExist(destinationBucketName);
            
            string sourceBucket = Path.Combine(_options.Path
                                             , sourceBucketName);
            string sourcePath = Path.Combine(sourceBucket
                                           , sourceFileName);
            string destinationBucket = Path.Combine(_options.Path
                                                  , destinationBucketName);
            string destinationPath = Path.Combine(destinationBucket
                                                , destinationFileName);
            try
            {
                if (!Directory.Exists(sourceBucket))
                {
                    throw new DirectoryNotFoundException($"{sourceBucket} does not exists.");
                }
                if (!Directory.Exists(destinationBucket))
                {
                    throw new DirectoryNotFoundException($"{destinationBucket} does not exists.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(sourcePath, destinationPath, true);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{nameof(CopyFile)} : An error occured while moving {{source}} to {{destination}}", sourcePath, destinationPath);
                throw;
            }
        }

	    public List<ICloudFileMetadata> GetFileList(string prefix = null, string bucketName = "")
	    {
	        EnsureBucketExist(bucketName);
	        var output = new List<ICloudFileMetadata>();
	        try
	        {
	            var files = Directory.GetFiles(RootPath
	                                         , Constants.WildCardStar
	                                         , SearchOption.AllDirectories)
	                                 .ToList();
	            if (!string.IsNullOrWhiteSpace(prefix))
	            {
	                string modifiedPrefix = Path.Combine(RootPath
	                                                   , prefix?.Replace(Path.AltDirectorySeparatorChar
	                                                                   , Path.DirectorySeparatorChar));
	                files = files.Where(f => f.StartsWith(modifiedPrefix))
	                             .ToList();
	            }

	            foreach (string file in files)
	            {
	                var fileInfo = new FileInfo(file);
	                output.Add(new CloudFileMetadata
	                           {
	                               Bucket = _bucketName
	                             , Size = (ulong)fileInfo.Length
	                             , Name = GetFileName(fileInfo.FullName)
	                             , LastModified = fileInfo.LastWriteTimeUtc
	                             , ContentType = MimeTypesMap.GetMimeType(fileInfo.Name)
	                             , SignedUrl = fileInfo.FullName
	                           });
	            }
	        }
	        catch (Exception e)
	        {
	            _logger.LogError(e
	                           , e.Message);
	            throw;
	        }

	        return output;
	    }
	}
}
