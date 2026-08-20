using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Extensions.Configuration.Unit.Tests;

[TestClass]
public sealed class KubernetesConfigurationTests
{
    [TestMethod]
    public void AddKubernetesConfiguration_ConfigMapAndSecretDefineSameKey_SecretWins()
    {
        var testRoot = CreateConfigurationTree(
            configMapJson: """{"Credentials":{"Password":"configmap-value"}}""",
            secretJson: """{"Credentials":{"Password":"secret-value"}}""");

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(testRoot)
                .AddKubernetesConfiguration()
                .Build();

            Assert.AreEqual("secret-value", configuration["Credentials:Password"]);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public void AddKubernetesConfiguration_OnlyConfigMapExists_LoadsConfigMapValue()
    {
        var testRoot = CreateConfigurationTree(
            configMapJson: """{"Feature":{"Enabled":true}}""",
            secretJson: null);

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(testRoot)
                .AddKubernetesConfiguration()
                .Build();

            Assert.AreEqual("True", configuration["Feature:Enabled"]);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public void AddKubernetesConfiguration_NullBuilder_ThrowsArgumentNullException()
    {
        IConfigurationBuilder builder = null!;

        Assert.ThrowsExactly<ArgumentNullException>(() => builder.AddKubernetesConfiguration());
    }

    private static string CreateConfigurationTree(string configMapJson, string? secretJson)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"ruya-configuration-{Guid.NewGuid():N}");
        var configMapDirectory = Path.Combine(testRoot, "configuration", "configmap");
        Directory.CreateDirectory(configMapDirectory);
        File.WriteAllText(Path.Combine(configMapDirectory, "appsettings.ConfigMap.json"), configMapJson);

        if (secretJson is not null)
        {
            var secretDirectory = Path.Combine(testRoot, "configuration", "secret");
            Directory.CreateDirectory(secretDirectory);
            File.WriteAllText(Path.Combine(secretDirectory, "appsettings.Secrets.json"), secretJson);
        }

        return testRoot;
    }
}
