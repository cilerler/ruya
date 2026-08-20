using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.Serialization;

namespace Ruya.Services.MessageQueue.MsSql.Integration.Tests;

[TestClass]
public sealed class MsSqlProviderContractTests
{
    [TestMethod]
    public void PublicApi_ReleasedProviderConstructor_RemainsAsObsoleteBridge()
    {
        var constructor = typeof(MsSqlProvider).GetConstructor(
        [
            typeof(IOptions<MsSqlOptions>),
            typeof(IMessageSerializer),
            typeof(IEnumerable<IMessageMiddleware>),
            typeof(ILogger<MsSqlProvider>),
        ]);

        Assert.IsNotNull(constructor);
        Assert.IsNotNull(constructor.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
        // Intentional reflection name: verify a released obsolete member without compile-time use or CS0618.
        var poolingProperty = typeof(MsSqlOptions).GetProperty("EnableConversationPooling");
        Assert.IsNotNull(poolingProperty);
        Assert.IsNotNull(poolingProperty.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
    }

    [TestMethod]
    public void AddMsSql_InvalidOptionsResolved_FailsTypedValidation()
    {
        var services = new ServiceCollection();
        services.AddMessageQueue(_ => { }).AddMsSql(options =>
        {
            options.ConnectionString = string.Empty;
        });
        using var provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<MsSqlOptions>>().Value);
    }

    [TestMethod]
    public void AddMsSql_UnsupportedConversationPoolingEnabled_FailsTypedValidation()
    {
        var services = new ServiceCollection();
        services.AddMessageQueue(_ => { }).AddMsSql(options =>
        {
            options.ConnectionString = "Server=localhost;Integrated Security=true;TrustServerCertificate=true";
            // Intentional reflection name: exercise a released obsolete member without compile-time use or CS0618.
            typeof(MsSqlOptions).GetProperty("EnableConversationPooling")!.SetValue(options, true);
        });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<MsSqlOptions>>().Value);
        StringAssert.Contains(exception.Message, "not supported", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void AddMsSql_ConfigurationSectionPresent_BindsTypedOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBrokerMessageQueue"] = "Server=localhost;Integrated Security=true",
                [$"{MsSqlOptions.ConfigurationSectionName}:MessageQueueConnectionStringKey"] = "ServiceBrokerMessageQueue",
                [$"{MsSqlOptions.ConfigurationSectionName}:ReceiveTimeoutMs"] = "250",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMessageQueue(_ => { }).AddMsSql();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<MsSqlOptions>>().Value;
        Assert.AreEqual("Server=localhost;Integrated Security=true", options.ConnectionString);
        Assert.AreEqual("ServiceBrokerMessageQueue", options.MessageQueueConnectionStringKey);
        Assert.AreEqual(250, options.ReceiveTimeoutMs);
    }

    [TestMethod]
    public void AddMsSql_TypedConfigurationWithoutConfigurationService_PreservesReleasedBehavior()
    {
        var services = new ServiceCollection();
        services.AddMessageQueue(_ => { }).AddMsSql(options =>
        {
            options.ConnectionString = "Server=localhost;Integrated Security=true";
        });
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<MsSqlOptions>>().Value;

        Assert.AreEqual("Server=localhost;Integrated Security=true", options.ConnectionString);
    }

    [TestMethod]
    public void AddMsSql_ConnectionStringKeyMissingFromCatalog_FailsWithoutEchoingSecrets()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{MsSqlOptions.ConfigurationSectionName}:MessageQueueConnectionStringKey"] = "MissingServiceBroker",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMessageQueue(_ => { }).AddMsSql();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<MsSqlOptions>>().Value);

        StringAssert.Contains(exception.Message, "MessageQueueConnectionStringKey", StringComparison.Ordinal);
        Assert.IsFalse(exception.Message.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void EmbeddedSchema_CurrentTopologyAndOperationProcedures_ArePackagedTogether()
    {
        var assembly = typeof(MsSqlProvider).Assembly;
        var assemblyName = assembly.GetName().Name
            ?? throw new InvalidOperationException("The SQL Server message-queue assembly must have a name.");
        var schemaName = $"{assemblyName}.Resources.SQL.ServiceBrokerSchema.sql";
        using var stream = assembly.GetManifestResourceStream(schemaName);
        Assert.IsNotNull(stream);
        using var reader = new StreamReader(stream);
        var schema = reader.ReadToEnd();

        StringAssert.Contains(schema, "@ProposedQueueName SYSNAME");
        StringAssert.Contains(schema, "RuyaServicesMessageQueue_SendMessage");
        StringAssert.Contains(schema, "RuyaServicesMessageQueue_ReceiveMessage");
        Assert.IsFalse(schema.Contains("@QueueName,\n                @ServiceName", StringComparison.Ordinal));
    }

    [TestMethod]
    [SuppressMessage(
        "Maintainability",
        "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "The regression inspects private schema command construction without widening the package API surface.")]
    public void EmbeddedSchema_StandaloneGoBatches_KeepProcedureCreationFirstAndParameterless()
    {
        var assembly = typeof(MsSqlProvider).Assembly;
        var assemblyName = assembly.GetName().Name
            ?? throw new InvalidOperationException("The SQL Server message-queue assembly must have a name.");
        var schemaName = $"{assemblyName}.Resources.SQL.ServiceBrokerSchema.sql";
        using var stream = assembly.GetManifestResourceStream(schemaName);
        Assert.IsNotNull(stream);
        using var reader = new StreamReader(stream);
        var schema = reader.ReadToEnd();
        var splitMethod = typeof(MsSqlProvider).GetMethod(
            "SplitSchemaBatches",
            BindingFlags.Static | BindingFlags.NonPublic);
        var createCommandMethod = typeof(MsSqlProvider).GetMethod(
            "CreateSchemaBatchCommand",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.IsNotNull(splitMethod);
        Assert.IsNotNull(createCommandMethod);
        var batches = (IReadOnlyList<string>)splitMethod.Invoke(null, [schema])!;

        Assert.HasCount(12, batches);
        Assert.IsFalse(batches.Any(batch => batch.Split('\n')
            .Any(line => string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))));

        var procedureBatches = batches
            .Where(batch => batch.Contains("CREATE OR ALTER PROCEDURE", StringComparison.Ordinal))
            .ToArray();
        Assert.HasCount(3, procedureBatches);

        using var connection = new SqlConnection();
        foreach (var procedureBatch in procedureBatches)
        {
            StringAssert.StartsWith(
                procedureBatch.TrimStart(),
                "CREATE OR ALTER PROCEDURE",
                StringComparison.Ordinal);
            using var command = (SqlCommand)createCommandMethod.Invoke(
                null,
                [procedureBatch, connection, 30])!;
            Assert.AreEqual(0, command.Parameters.Count);
        }

        using var debugCommand = (SqlCommand)createCommandMethod.Invoke(
            null,
            [batches[0], connection, 30])!;
        Assert.AreEqual(1, debugCommand.Parameters.Count);
        Assert.AreEqual("@p0", debugCommand.Parameters[0].ParameterName);
    }

    [TestMethod]
    public void EmbeddedSqlResources_EveryScript_HasDocumentedDebugContract()
    {
        var assembly = typeof(MsSqlProvider).Assembly;
        var assemblyName = assembly.GetName().Name
            ?? throw new InvalidOperationException("The SQL Server message-queue assembly must have a name.");
        var prefix = $"{assemblyName}.Resources.SQL.";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(8, resources);
        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.IsNotNull(stream, resource);
            using var reader = new StreamReader(stream);
            var script = reader.ReadToEnd();

            StringAssert.StartsWith(script, "--!", resource);
            StringAssert.Contains(script, "--! Parameters:", StringComparison.Ordinal, resource);
            StringAssert.Contains(script, "@p0", StringComparison.Ordinal, resource);
            StringAssert.Contains(script, "-- DEBUG: Uncomment this block", StringComparison.Ordinal, resource);
            StringAssert.Contains(script, "DECLARE @Debug BIT = COALESCE(@p", StringComparison.Ordinal, resource);
        }
    }

    [TestMethod]
    public void EmbeddedTestSqlResources_EveryScript_HasDocumentedDebugContract()
    {
        var assembly = typeof(MsSqlProviderContractTests).Assembly;
        var assemblyName = assembly.GetName().Name
            ?? throw new InvalidOperationException("The SQL Server message-queue test assembly must have a name.");
        var prefix = $"{assemblyName}.Resources.SQL.";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(7, resources);
        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.IsNotNull(stream, resource);
            using var reader = new StreamReader(stream);
            var script = reader.ReadToEnd();

            StringAssert.StartsWith(script, "--!", resource);
            StringAssert.Contains(script, "--! Parameters:", StringComparison.Ordinal, resource);
            StringAssert.Contains(script, "@p0", StringComparison.Ordinal, resource);
            StringAssert.Contains(script, "-- DEBUG: Uncomment this block", StringComparison.Ordinal, resource);
            StringAssert.Contains(script, "DECLARE @Debug BIT = COALESCE(@p", StringComparison.Ordinal, resource);
        }
    }
}
