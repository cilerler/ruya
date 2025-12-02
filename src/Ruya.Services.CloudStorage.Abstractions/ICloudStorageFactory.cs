using System;

namespace Ruya.Services.CloudStorage.Abstractions;

public interface ICloudStorageFactory
{
    /// <summary>
    /// Gets the cloud storage service for the specified provider key.
    /// </summary>
    /// <param name="providerKey">The key of the provider (e.g., "Amazon", "Azure", "Google", "Local").</param>
    /// <returns>The cloud storage service.</returns>
    ICloudFileService GetService(string providerKey);
}
