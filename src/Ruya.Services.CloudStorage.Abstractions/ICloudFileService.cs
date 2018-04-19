using System;
using System.Collections.Generic;
using System.IO;

namespace Ruya.Services.CloudStorage.Abstractions
{
	public interface ICloudFileService
	{
		/// <summary>
		/// Allows switching/setting remote storage bucket.
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		void SetBucket(string name);

		/// <summary>
		/// Gets the file's metadata, including a presigned url for downloading
		/// </summary>
		/// <param name="fileName"></param>
		/// <param name="bucketName"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentException">Thrown when argument is not exist</exception>
		ICloudFileMetadata GetFileMetadata(string fileName, string bucketName = "");

        /// <summary>
        /// Lists all the files
        /// </summary>
        /// <param name="prefix">Prefix of the remote file</param>
        /// <param name="bucketName"></param>
        List<ICloudFileMetadata> GetFileList(string prefix = null, string bucketName = "");

        /// <summary>
        /// Deletes remote file to the provided target stream
        /// </summary>
        /// <param name="fileName">Full file path to remote file</param>
        /// <param name="bucketName"></param>
        void DeleteFile(string fileName, string bucketName = "");

        /// <summary>
        /// Copies given remote file to the provided bucket
        /// </summary>
        /// <param name="sourceBucketName"></param>
        /// <param name="sourceFileName">Full file path to remote source file<</param>
        /// <param name="destinationBucketName"></param>
        /// <param name="destinationFileName">Full file path to remote destination file<</param>
        void CopyFile(string sourceBucketName, string sourceFileName, string destinationBucketName, string destinationFileName);

		/// <summary>
		/// Downloads remote file to the provided target stream
		/// </summary>
		/// <param name="fileName">Full file path to remote file</param>
		/// <param name="targetStream">Any writeable stream (MemoryStream, FileStream)</param>
		/// <param name="bucketName"></param>
		void DownloadFile(string fileName, Stream targetStream, string bucketName = "");

		/// <summary>
		/// Generic method for uploading file to remote storage.
		/// </summary>
		/// <param name="sourcePath">Full path to local file to upload.</param>
		/// <param name="targetPath">Target folder in remote storage, if file extension does not exist, will use the fileName in sourcePath</param>
		/// <param name="bucketName"></param>
		/// <returns></returns>
		ICloudFileMetadata UploadFile(string sourcePath, string targetPath, string bucketName = "");

		/// <summary>
		/// Generic method for uploading stream to remote storage.
		/// </summary>
		/// <param name="sourceStream">Full path to local file to upload.</param>
		/// <param name="targetPath">Target folder in remote storage</param>
		/// <param name="contentType">The content type to use (text/plain by default).</param>
		/// <param name="bucketName"></param>
		/// <returns></returns>
		ICloudFileMetadata UploadStream(Stream sourceStream, string targetPath, string contentType, string bucketName = "");
	}
}
