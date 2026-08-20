using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Extensions;

namespace Ruya.Services.MessageQueue.RabbitMq.Unit.Tests;

[TestClass]
public sealed class RabbitMQOptionsTests
{
    private const string RabbitMqUsername = "useradmin";
    private const string RabbitMqPassword = "passwordadmin";

    [TestMethod]
    public void Validate_CredentialsOmitted_ReturnsFailure()
    {
        // Arrange
        var validator = new RabbitMQOptionsValidator();
        var options = CreateValidOptions();
        options.Username = null!;
        options.Password = null!;

        // Act
        var result = validator.Validate(Options.DefaultName, options);

        // Assert
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Username", StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow(" ", RabbitMqPassword, "Username")]
    [DataRow(RabbitMqUsername, " ", "Password")]
    public void Validate_CredentialBlank_ReturnsFailure(
        string username,
        string password,
        string expectedCredential)
    {
        // Arrange
        var validator = new RabbitMQOptionsValidator();
        var options = CreateValidOptions();
        options.Username = username;
        options.Password = password;

        // Act
        var result = validator.Validate(Options.DefaultName, options);

        // Assert
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, expectedCredential, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Validate_CredentialsExplicit_ReturnsSuccess()
    {
        // Arrange
        var validator = new RabbitMQOptionsValidator();
        var options = CreateValidOptions();

        // Act
        var result = validator.Validate(Options.DefaultName, options);

        // Assert
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    [DataRow("Host")]
    [DataRow("VirtualHost")]
    [DataRow("DefaultExchangeType")]
    public void Validate_RequiredDomainValueBlank_ReturnsFailure(string propertyName)
    {
        // Arrange
        var validator = new RabbitMQOptionsValidator();
        var options = CreateValidOptions();
        switch (propertyName)
        {
            case "Host":
                options.Host = " ";
                break;
            case "VirtualHost":
                options.VirtualHost = " ";
                break;
            case "DefaultExchangeType":
                options.DefaultExchangeType = " ";
                break;
            default:
                Assert.Fail($"Unexpected property {propertyName}.");
                break;
        }

        // Act
        var result = validator.Validate(Options.DefaultName, options);

        // Assert
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, propertyName, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Validate_StreamsEnabled_ReturnsFailure()
    {
        // Arrange
        var validator = new RabbitMQOptionsValidator();
        var options = CreateValidOptions();
        options.UseStreams = true;

        // Act
        var result = validator.Validate(Options.DefaultName, options);

        // Assert
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Streams", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Validate_StreamOptionsPresent_ReturnsFailure()
    {
        // Arrange
        var validator = new RabbitMQOptionsValidator();
        var options = CreateValidOptions();
        options.StreamOptions = new StreamOptions();

        // Act
        var result = validator.Validate(Options.DefaultName, options);

        // Assert
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Streams", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Validate_PublisherConfirmTimeoutNonPositive_ReturnsFailure()
    {
        // Arrange
        var validator = new RabbitMQOptionsValidator();
        var options = CreateValidOptions();
        options.UsePublisherConfirms = true;
        options.PublisherConfirmTimeout = TimeSpan.Zero;

        // Act
        var result = validator.Validate(Options.DefaultName, options);

        // Assert
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "PublisherConfirmTimeout", StringComparison.Ordinal);
    }

    [TestMethod]
    public void Validate_PublisherConfirmTimeoutNonPositiveWithConfirmsDisabled_ReturnsSuccess()
    {
        // Arrange
        var validator = new RabbitMQOptionsValidator();
        var options = CreateValidOptions();
        options.UsePublisherConfirms = false;
        options.PublisherConfirmTimeout = TimeSpan.Zero;

        // Act
        var result = validator.Validate(Options.DefaultName, options);

        // Assert
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public async Task AddRabbitMQ_ConfigurationSectionPresent_BindsOptions()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{RabbitMQOptions.ConfigurationSectionName}:Host"] = "rabbitmq.internal",
            [$"{RabbitMQOptions.ConfigurationSectionName}:Port"] = "5671",
            [$"{RabbitMQOptions.ConfigurationSectionName}:VirtualHost"] = "/orders",
            [$"{RabbitMQOptions.ConfigurationSectionName}:Username"] = RabbitMqUsername,
            [$"{RabbitMQOptions.ConfigurationSectionName}:Password"] = RabbitMqPassword,
            [$"{RabbitMQOptions.ConfigurationSectionName}:UseSsl"] = "true",
            [$"{RabbitMQOptions.ConfigurationSectionName}:ConnectionTimeout"] = "00:00:07",
            [$"{RabbitMQOptions.ConfigurationSectionName}:Heartbeat"] = "00:00:45",
            [$"{RabbitMQOptions.ConfigurationSectionName}:AutomaticRecoveryEnabled"] = "false",
            [$"{RabbitMQOptions.ConfigurationSectionName}:NetworkRecoveryInterval"] = "00:00:09",
            [$"{RabbitMQOptions.ConfigurationSectionName}:ChannelPoolSize"] = "17",
            [$"{RabbitMQOptions.ConfigurationSectionName}:UseStreams"] = "false"
        });
        RegisterRabbitMQ(builder.Services).AddRabbitMQ();
        using var host = builder.Build();

        // Act
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<RabbitMQOptions>>().Value;

        // Assert
        Assert.AreEqual("rabbitmq.internal", options.Host);
        Assert.AreEqual(5671, options.Port);
        Assert.AreEqual("/orders", options.VirtualHost);
        Assert.AreEqual(RabbitMqUsername, options.Username);
        Assert.AreEqual(RabbitMqPassword, options.Password);
        Assert.IsTrue(options.UseSsl);
        Assert.AreEqual(TimeSpan.FromSeconds(7), options.ConnectionTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(45), options.Heartbeat);
        Assert.IsFalse(options.AutomaticRecoveryEnabled);
        Assert.AreEqual(TimeSpan.FromSeconds(9), options.NetworkRecoveryInterval);
        Assert.AreEqual(17, options.ChannelPoolSize);
        Assert.IsFalse(options.UseStreams);

        await host.StopAsync();
    }

    [TestMethod]
    public async Task AddRabbitMQ_InvalidCredentials_ThrowsDuringHostStartup()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{RabbitMQOptions.ConfigurationSectionName}:Host"] = "unreachable.invalid",
            [$"{RabbitMQOptions.ConfigurationSectionName}:VirtualHost"] = "/",
            [$"{RabbitMQOptions.ConfigurationSectionName}:Username"] = " ",
            [$"{RabbitMQOptions.ConfigurationSectionName}:Password"] = RabbitMqPassword
        });
        RegisterRabbitMQ(builder.Services).AddRabbitMQ();
        using var host = builder.Build();

        // Act
        var exception = await Assert.ThrowsExactlyAsync<OptionsValidationException>(
            () => host.StartAsync());

        // Assert
        StringAssert.Contains(exception.Message, "Username", StringComparison.Ordinal);
    }

    [TestMethod]
    public void CreateConnectionFactory_TransportOptionsConfigured_MapsOptions()
    {
        // Arrange
        var options = CreateValidOptions();
        options.Host = "rabbitmq.internal";
        options.Port = 5671;
        options.VirtualHost = "/orders";
        options.UseSsl = true;
        options.ConnectionTimeout = TimeSpan.FromSeconds(7);
        options.Heartbeat = TimeSpan.FromSeconds(45);
        options.AutomaticRecoveryEnabled = false;
        options.NetworkRecoveryInterval = TimeSpan.FromSeconds(9);
        // Act
        var factory = RabbitMQProvider.CreateConnectionFactory(options);

        // Assert
        Assert.AreEqual(options.Host, factory.HostName);
        Assert.AreEqual(options.Port, factory.Port);
        Assert.AreEqual(options.VirtualHost, factory.VirtualHost);
        Assert.AreEqual(options.Username, factory.UserName);
        Assert.AreEqual(options.Password, factory.Password);
        Assert.AreEqual(options.ConnectionTimeout, factory.RequestedConnectionTimeout);
        Assert.AreEqual(options.Heartbeat, factory.RequestedHeartbeat);
        Assert.AreEqual(options.AutomaticRecoveryEnabled, factory.AutomaticRecoveryEnabled);
        Assert.AreEqual(options.NetworkRecoveryInterval, factory.NetworkRecoveryInterval);
        Assert.IsTrue(factory.Ssl.Enabled);
        Assert.AreEqual(options.Host, factory.Ssl.ServerName);
    }

    private static RabbitMQOptions CreateValidOptions() => new()
    {
        Host = "localhost",
        VirtualHost = "/",
        Username = RabbitMqUsername,
        Password = RabbitMqPassword
    };

    private static IMessageQueueBuilder RegisterRabbitMQ(IServiceCollection services) =>
        services.AddMessageQueue(options =>
        {
            options.Providers["rabbitmq"] = new ProviderConfiguration
            {
                Type = "RabbitMQ",
                Enabled = true
            };
        });
}
