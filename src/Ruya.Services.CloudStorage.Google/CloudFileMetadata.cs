using System;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.Google;

public class CloudFileMetadata : ICloudFileMetadata
{
	public string Bucket { get; set; }
	public string Name { get; set; }
	public ulong? Size { get; set; }
	public DateTime? LastModified { get; set; }
	public string ContentType { get; set; }
	public string SignedUrl { get; set; }
}
