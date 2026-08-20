using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.CloudStorage.Abstractions;

namespace Ruya.Services.CloudStorage.UnitTests;

[TestClass]
public class CloudStorageFactoryTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void GetService_WithInvalidProviderKey_ThrowsArgumentException(string? providerKey)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICloudStorageFactory, CloudStorageFactory>();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ICloudStorageFactory>();

        Assert.Throws<ArgumentException>(() => factory.GetService(providerKey!));
    }

    [TestMethod]
    public void GetService_WithRegisteredProvider_ReturnsService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ICloudFileService, FakeCloudFileService>("TestProvider");
        services.AddSingleton<ICloudStorageFactory, CloudStorageFactory>();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ICloudStorageFactory>();

        // Act
        var service = factory.GetService("TestProvider");

        // Assert
        Assert.IsNotNull(service);
        Assert.IsInstanceOfType(service, typeof(FakeCloudFileService));
    }

    [TestMethod]
    public void GetService_WithUnregisteredProvider_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ICloudStorageFactory, CloudStorageFactory>();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ICloudStorageFactory>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => factory.GetService("NonExistentProvider"));

        Assert.IsTrue(exception.Message.Contains("NonExistentProvider"));
        Assert.IsTrue(exception.Message.Contains("not registered"));
    }

    [TestMethod]
    public void GetService_WithMultipleProviders_ReturnsCorrectService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ICloudFileService, FakeCloudFileService>("Provider1");
        services.AddKeyedSingleton<ICloudFileService, AnotherFakeCloudFileService>("Provider2");
        services.AddSingleton<ICloudStorageFactory, CloudStorageFactory>();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ICloudStorageFactory>();

        // Act
        var service1 = factory.GetService("Provider1");
        var service2 = factory.GetService("Provider2");

        // Assert
        Assert.IsInstanceOfType(service1, typeof(FakeCloudFileService));
        Assert.IsInstanceOfType(service2, typeof(AnotherFakeCloudFileService));
    }

    [TestMethod]
    public void GetService_WithCaseSensitiveKey_RespectsCase()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ICloudFileService, FakeCloudFileService>("Amazon");
        services.AddSingleton<ICloudStorageFactory, CloudStorageFactory>();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ICloudStorageFactory>();

        // Act & Assert - Keys are case-sensitive
        Assert.Throws<ArgumentException>(() => factory.GetService("amazon"));
        Assert.Throws<ArgumentException>(() => factory.GetService("AMAZON"));

        // Correct case works
        var service = factory.GetService("Amazon");
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void GetService_CalledMultipleTimes_ReturnsSameInstanceForSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ICloudFileService, FakeCloudFileService>("Singleton");
        services.AddSingleton<ICloudStorageFactory, CloudStorageFactory>();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ICloudStorageFactory>();

        // Act
        var service1 = factory.GetService("Singleton");
        var service2 = factory.GetService("Singleton");

        // Assert
        Assert.AreSame(service1, service2);
    }

    [TestMethod]
    public void GetService_CalledMultipleTimes_ReturnsDifferentInstanceForTransient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedTransient<ICloudFileService, FakeCloudFileService>("Transient");
        services.AddSingleton<ICloudStorageFactory, CloudStorageFactory>();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ICloudStorageFactory>();

        // Act
        var service1 = factory.GetService("Transient");
        var service2 = factory.GetService("Transient");

        // Assert
        Assert.AreNotSame(service1, service2);
    }
}
