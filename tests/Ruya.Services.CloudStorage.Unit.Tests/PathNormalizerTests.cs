using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.UnitTests;

[TestClass]
public class PathNormalizerTests
{
    [TestMethod]
    [DataRow(@"folder\subfolder\file.txt", "folder/subfolder/file.txt")]
    [DataRow("folder/subfolder/file.txt", "folder/subfolder/file.txt")]
    [DataRow(@"\folder\subfolder\file.txt", "folder/subfolder/file.txt")]
    [DataRow("/folder/subfolder/file.txt", "folder/subfolder/file.txt")]
    [DataRow(@"folder\\subfolder\\\file.txt", "folder/subfolder/file.txt")]
    [DataRow("folder//subfolder///file.txt", "folder/subfolder/file.txt")]
    [DataRow(@"folder\subfolder\", "folder/subfolder/")]
    [DataRow("folder/subfolder/", "folder/subfolder/")]
    [DataRow("", "")]
    [DataRow("/", "")]
    [DataRow(@"\", "")]
    [DataRow("///", "")]
    [DataRow(@"\\\", "")]
    public void ToCloudPath_PreservesReleasedNormalizationSemantics(
        string path,
        string expected)
    {
        Assert.AreEqual(expected, PathNormalizer.ToCloudPath(path));
    }

    [TestMethod]
    public void CombineCloudPath_PreservesFileNameAsOpaqueValue()
    {
        Assert.AreEqual(
            "folder/subfolder/file\\name.txt",
            PathNormalizer.CombineCloudPath(@"\folder\subfolder\", @"file\name.txt"));
    }
}
