using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Amazon;
using AzureSetting = Ruya.Services.CloudStorage.Azure.Setting;
using GoogleSettings = Ruya.Services.CloudStorage.Google.StorageServiceSettings;

namespace Ruya.Services.CloudStorage.UnitTests;

[TestClass]
public sealed class CloudStorageRegistrationTests
{
    [TestMethod]
    public void AddAmazonStorageService_WhenOnlyOneStaticCredentialIsConfigured_FailsOptionsValidation()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudStorage:Amazon:AccessKey"] = "access-only"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddAmazonStorageService();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<Setting>>().Value);
    }

    [TestMethod]
    public void AddLocalStorageService_BindsCanonicalConfigurationSection()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudStorage:Local:Path"] = "storage-root"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLocalStorageService();
        using ServiceProvider provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<IOptions<Ruya.Services.CloudStorage.Local.StorageServiceSettings>>().Value;
        Assert.AreEqual("storage-root", settings.Path);
    }

    [TestMethod]
    public void AddAzureStorageService_ResolvesConnectionStringThroughCatalog()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudStorage:Azure:ConnectionStringKey"] = "BlobStorage",
                ["ConnectionStrings:BlobStorage"] = "UseDevelopmentStorage=true"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddAzureStorageService();
        using ServiceProvider provider = services.BuildServiceProvider();

        AzureSetting settings = provider.GetRequiredService<IOptions<AzureSetting>>().Value;
        Assert.AreEqual("BlobStorage", settings.ConnectionStringKey);
    }

    [TestMethod]
    public void AddAzureStorageService_WhenCatalogEntryIsMissing_FailsOptionsValidation()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudStorage:Azure:ConnectionStringKey"] = "MissingBlobStorage"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddAzureStorageService();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AzureSetting>>().Value);
    }

    [TestMethod]
    public void AddAzureStorageService_WithExplicitConfiguration_ResolvesWithoutGlobalConfigurationRegistration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudStorage:Azure:ConnectionStringKey"] = "BlobStorage",
                ["ConnectionStrings:BlobStorage"] = "UseDevelopmentStorage=true"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAzureStorageService(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        ICloudFileService client = provider.GetRequiredKeyedService<ICloudFileService>(AzureSetting.ProviderName);

        Assert.IsInstanceOfType<Ruya.Services.CloudStorage.Azure.Client>(client);
        Assert.IsNull(provider.GetService<IConfiguration>());
    }

    [TestMethod]
    public void AddGoogleStorageService_BindsHierarchicalSecretCredential()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudStorage:Google:Credential:type"] = "service_account",
                ["CloudStorage:Google:Credential:project_id"] = "local-test"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddGoogleStorageService();
        using ServiceProvider provider = services.BuildServiceProvider();

        GoogleSettings settings = provider.GetRequiredService<IOptions<GoogleSettings>>().Value;
        StringAssert.Contains(settings.Credential, "service_account");
        StringAssert.Contains(settings.Credential, "local-test");
    }
}
