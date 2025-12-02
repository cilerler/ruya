using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;
using Ruya.Services.CloudStorage.Tests.Common;

namespace Ruya.Services.CloudStorage.Amazon.Tests;

[TestClass]
public class ClientTest : CloudStorageTestBase
{
    protected override ICloudFileService GetClient()
    {
        var factory = ScopeServiceProvider.GetRequiredService<ICloudStorageFactory>();
        return factory.GetService(Setting.ProviderName);
    }

    protected override string GetBucketName() => "mybucket";
}
