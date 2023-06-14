using System;

namespace Ruya.Services.CloudStorage.Abstractions;

public interface ICloudFileMetadata
{
	string Bucket { get; set; }
	string Name { get; set; }
	ulong? Size { get; set; }
	DateTime? LastModified { get; set; }
	string ContentType { get; set; }
	string SignedUrl { get; set; }
}
