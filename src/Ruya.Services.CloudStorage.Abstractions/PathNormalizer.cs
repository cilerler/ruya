using System;
using System.IO;

namespace Ruya.Services.CloudStorage.Abstractions;

/// <summary>
/// Provides utility methods for normalizing file paths for cloud storage operations.
/// </summary>
public static class PathNormalizer
{
    /// <summary>
    /// Normalizes a local file path to use forward slashes, which is the standard for cloud storage.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path with forward slashes.</returns>
    /// <example>
    /// <code>
    /// // On Windows:
    /// PathNormalizer.ToCloudPath(@"folder\subfolder\file.txt");
    /// // Returns: "folder/subfolder/file.txt"
    /// </code>
    /// </example>
    public static string ToCloudPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        string fileName = Path.GetFileName(path);
        string? directoryName = Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(directoryName))
            return fileName;

        string normalizedDirectory = directoryName
            .Replace(Path.DirectorySeparatorChar, '/')
            .Trim('/');

        return $"{normalizedDirectory}/{fileName}";
    }

    /// <summary>
    /// Combines a directory and file name, normalizing to cloud path format.
    /// </summary>
    /// <param name="directory">The directory path.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The combined and normalized path.</returns>
    public static string CombineCloudPath(string? directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return fileName;

        string normalizedDirectory = directory
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace('\\', '/')
            .Trim('/');

        return $"{normalizedDirectory}/{fileName}";
    }
}
