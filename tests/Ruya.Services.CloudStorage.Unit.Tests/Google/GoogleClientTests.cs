using System;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Google;

namespace Ruya.Services.CloudStorage.Google.Integration.Tests;

/// <summary>
/// Unit tests for Google Cloud Storage client.
/// Note: The Google Cloud Storage library's sealed classes (StorageClient, UrlSigner)
/// make mocking extremely difficult. These tests focus on testable aspects.
/// For full integration testing, use the integration test project with the GCS emulator.
/// </summary>
[TestClass]
public class GoogleClientTests
{
    #region Setting Tests

    [TestMethod]
    public void Setting_ProviderName_IsGoogle()
    {
        Assert.AreEqual("Google", StorageServiceSettings.ProviderName);
    }

    [TestMethod]
    public void Setting_ConfigurationSectionName_IsCloudStorageGoogle()
    {
        Assert.AreEqual("CloudStorage:Google", StorageServiceSettings.ConfigurationSectionName);
    }

    #endregion

    #region Path Normalization Tests

    [TestMethod]
    public void PathNormalization_WindowsPathShouldBeConvertedToForwardSlashes()
    {
        // This tests the path normalization logic used in UploadStreamAsync
        // The actual normalization happens in the Client but we can test the pattern
        var windowsPath = @"folder\subfolder\file.txt";
        var expectedCloudPath = "folder/subfolder/file.txt";

        // Simulate the normalization logic from the client
        var fileName = Path.GetFileName(windowsPath);
        var directoryName = Path.GetDirectoryName(windowsPath) ?? "";
        var correctedDirectoryName = directoryName
            .Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Trim(Path.AltDirectorySeparatorChar);
        var destinationFileName = (correctedDirectoryName + Path.AltDirectorySeparatorChar + fileName)
            .TrimStart(Path.AltDirectorySeparatorChar);

        // Assert
        Assert.AreEqual(expectedCloudPath, destinationFileName);
    }

    [TestMethod]
    public void PathNormalization_AlreadyForwardSlashes_RemainsUnchanged()
    {
        var cloudPath = "folder/subfolder/file.txt";

        var fileName = Path.GetFileName(cloudPath);
        var directoryName = Path.GetDirectoryName(cloudPath) ?? "";
        var correctedDirectoryName = directoryName
            .Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Trim(Path.AltDirectorySeparatorChar);
        var destinationFileName = (correctedDirectoryName + Path.AltDirectorySeparatorChar + fileName)
            .TrimStart(Path.AltDirectorySeparatorChar);

        Assert.AreEqual(cloudPath, destinationFileName);
    }

    [TestMethod]
    public void PathNormalization_FileInRoot_NoLeadingSlash()
    {
        var rootFile = "file.txt";

        var fileName = Path.GetFileName(rootFile);
        var directoryName = Path.GetDirectoryName(rootFile) ?? "";
        var correctedDirectoryName = directoryName
            .Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Trim(Path.AltDirectorySeparatorChar);
        var destinationFileName = string.IsNullOrEmpty(correctedDirectoryName)
            ? fileName
            : (correctedDirectoryName + Path.AltDirectorySeparatorChar + fileName).TrimStart(Path.AltDirectorySeparatorChar);

        Assert.AreEqual(rootFile, destinationFileName);
    }

    #endregion
}
