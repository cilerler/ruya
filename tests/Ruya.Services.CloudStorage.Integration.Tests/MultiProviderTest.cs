using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Azure;
using Ruya.Services.CloudStorage.Local;
using Ruya.Services.CloudStorage.Tests.Common;
using Ruya.Diagnostics.DistributedTracing;
using System.Diagnostics.Metrics;

namespace Ruya.Services.CloudStorage.Integration.Tests;

[TestClass]
public class MultiProviderTest
{
    [TestMethod]
    public void CanResolveMultipleProviders()
    {
        var myConfiguration = new Dictionary<string, string>
        {
            {"CloudStorage:Azure:ConnectionStringKey", "AzureStorage"},
            {"CloudStorage:Azure:Container", "mybucket"},
            {"ConnectionStrings:AzureStorage", "UseDevelopmentStorage=true"},
            {"CloudStorage:Local:Path", "/tmp"}
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IMeterFactory, StubMeterFactory>();
        services.AddSingleton<IDistributedTracing, StubDistributedTracing>();

        // Register both
        services.AddAzureStorageService();
        services.AddLocalStorageService();

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ICloudStorageFactory>();

        var azure = factory.GetService(Ruya.Services.CloudStorage.Azure.Setting.ProviderName);
        var local = factory.GetService(Ruya.Services.CloudStorage.Local.StorageServiceSettings.ProviderName);

        Assert.IsNotNull(azure);
        Assert.IsNotNull(local);
        Assert.IsInstanceOfType(azure, typeof(Ruya.Services.CloudStorage.Azure.Client));
        Assert.IsInstanceOfType(local, typeof(Ruya.Services.CloudStorage.Local.Client));
        Assert.AreNotSame(azure, local);
    }
}
