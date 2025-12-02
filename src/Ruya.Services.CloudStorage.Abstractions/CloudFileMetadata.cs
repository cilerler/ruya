using System;

namespace Ruya.Services.CloudStorage.Abstractions;

public record CloudFileMetadata(
    string Bucket,
    string Name,
    ulong? Size,
    DateTime? LastModified,
    string ContentType,
    string SignedUrl
);
