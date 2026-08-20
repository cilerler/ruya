using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Extensions.Configuration.Unit.Tests;

[TestClass]
public sealed class FeatureFlagTests
{
	[TestMethod]
	public void GetFeatureFlag_ConfiguredTrue_ReturnsTrue()
	{
		var configuration = BuildConfiguration(("FeatureManagement:Example", "true"));

		Assert.IsTrue(configuration.GetFeatureFlag<ExampleSettings>());
	}

	[TestMethod]
	public void GetFeatureFlag_MissingValue_ReturnsFalse()
	{
		var configuration = BuildConfiguration();

		Assert.IsFalse(configuration.GetFeatureFlag<ExampleSettings>());
	}

	[TestMethod]
	public void GetFeatureFlag_MalformedValue_ThrowsInvalidOperationException()
	{
		var configuration = BuildConfiguration(("FeatureManagement:Example", "not-a-boolean"));

		Assert.ThrowsExactly<InvalidOperationException>(() => configuration.GetFeatureFlag<ExampleSettings>());
	}

	[TestMethod]
	public void GetFeatureFlag_MissingFeatureFlagField_ThrowsInvalidOperationException()
	{
		var configuration = BuildConfiguration();

		Assert.ThrowsExactly<InvalidOperationException>(() => configuration.GetFeatureFlag<MissingFeatureFlagSettings>());
	}

	[TestMethod]
	public void GetFeatureFlag_BlankFeatureFlagField_ThrowsInvalidOperationException()
	{
		var configuration = BuildConfiguration();

		Assert.ThrowsExactly<InvalidOperationException>(() => configuration.GetFeatureFlag<BlankFeatureFlagSettings>());
	}

	[TestMethod]
	public void GetFeatureFlag_NullConfiguration_ThrowsArgumentNullException()
	{
		IConfiguration configuration = null!;

		Assert.ThrowsExactly<ArgumentNullException>(() => configuration.GetFeatureFlag<ExampleSettings>());
	}

	private static IConfiguration BuildConfiguration(params (string Key, string? Value)[] values)
	{
		var settings = new Dictionary<string, string?>();
		foreach (var (key, value) in values)
		{
			settings.Add(key, value);
		}

		return new ConfigurationBuilder()
			.AddInMemoryCollection(settings)
			.Build();
	}

}

public sealed class ExampleSettings
{
	public static readonly string FeatureFlag = nameof(ExampleSettings)[..^"Settings".Length];

	public bool Marker { get; init; }
}

public sealed class MissingFeatureFlagSettings
{
	public bool Marker { get; init; }
}

public sealed class BlankFeatureFlagSettings
{
	public static readonly string FeatureFlag = new(' ', 1);

	public bool Marker { get; init; }
}
