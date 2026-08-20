using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Extensions.Configuration.Unit.Tests;

[TestClass]
[DoNotParallelize]
public sealed class EnvironmentVariablesWithPrefixTests
{
	[TestMethod]
	public void AddEnvironmentVariablesWithPrefix_ConfiguredPrefix_LoadsAndStripsPrefix()
	{
		var prefix = $"RUYA_TEST_{Guid.NewGuid():N}_";
		var environmentVariableName = $"{prefix}Nested__Value";
		var originalValue = Environment.GetEnvironmentVariable(environmentVariableName);
		Environment.SetEnvironmentVariable(environmentVariableName, "from-environment");

		try
		{
			using var configuration = new ConfigurationManager();
			configuration.AddInMemoryCollection(new[]
			{
				new KeyValuePair<string, string?>(StartupExtensions.ConfigurationSectionName, prefix),
				new KeyValuePair<string, string?>("Nested:Value", "from-memory"),
			});

			var result = configuration.AddEnvironmentVariablesWithPrefix();

			Assert.AreSame(configuration, result);
			Assert.AreEqual("from-environment", configuration["Nested:Value"]);
		}
		finally
		{
			Environment.SetEnvironmentVariable(environmentVariableName, originalValue);
		}
	}

	[TestMethod]
	public void AddEnvironmentVariablesWithPrefix_MissingPrefix_DoesNotAddEnvironmentProvider()
	{
		using var configuration = new ConfigurationManager();
		configuration.AddInMemoryCollection(new[]
		{
			new KeyValuePair<string, string?>("Nested:Value", "from-memory"),
		});

		var providerCount = configuration.Sources.Count;
		var result = configuration.AddEnvironmentVariablesWithPrefix();

		Assert.AreSame(configuration, result);
		Assert.AreEqual(providerCount, configuration.Sources.Count);
		Assert.AreEqual("from-memory", configuration["Nested:Value"]);
	}

	[TestMethod]
	public void AddEnvironmentVariablesWithPrefix_NullConfiguration_ThrowsArgumentNullException()
	{
		IConfigurationManager configuration = null!;

		Assert.ThrowsExactly<ArgumentNullException>(() => configuration.AddEnvironmentVariablesWithPrefix());
	}
}
