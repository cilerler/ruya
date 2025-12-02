using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Ruya.Extensions.Configuration;

public static class FeatureFlags
{
	public const string ConfigurationSectionName = "FeatureManagement";
	public const string FeatureFlagSettingName = "FeatureFlag";

	public static bool GetFeatureFlag<T>(this IConfiguration configuration) where T : class
	{
		var featureFlagField = typeof(T).GetField(FeatureFlagSettingName, BindingFlags.Static | BindingFlags.Public) ?? throw new InvalidOperationException($"The feature flag field was not found on type {typeof(T).FullName}");

		var featureFlagValue = featureFlagField.GetValue(null)?.ToString();
		if (string.IsNullOrWhiteSpace(featureFlagValue))
		{
			throw new InvalidOperationException($"The feature flag value is null or empty for type {typeof(T).FullName}");
		}

		try
		{
			return configuration.GetValue<bool>($"{ConfigurationSectionName}:{featureFlagValue}");
		}
		catch (KeyNotFoundException)
		{
			return false;
		}
		catch (FormatException)
		{
			return false;
		}
	}
}
