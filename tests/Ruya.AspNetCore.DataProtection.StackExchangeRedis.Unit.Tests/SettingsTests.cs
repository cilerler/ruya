using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis.Unit.Tests;

[TestClass]
public class DataProtectionSettingsTests
{
	#region ConfigurationSectionName Tests

	[TestMethod]
	public void ConfigurationSectionName_IsNotNullOrEmpty()
	{
		// Assert
		Assert.IsFalse(string.IsNullOrEmpty(DataProtectionSettings.ConfigurationSectionName));
	}

	#endregion

	#region Default Values Tests

	[TestMethod]
	public void DefaultKeyLifetime_DefaultValue_Is90Days()
	{
		// Arrange
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys"
		};

		// Assert
		Assert.AreEqual(90, settings.DefaultKeyLifetime);
	}

	[TestMethod]
	public void Purposes_DefaultValue_IsEmptyDictionary()
	{
		// Arrange
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys"
		};

		// Assert
		Assert.IsNotNull(settings.Purposes);
		Assert.AreEqual(0, settings.Purposes.Count);
	}

	[TestMethod]
	public void ConnectionString_DefaultValue_IsNull()
	{
		// Arrange
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys"
		};

		// Assert
		Assert.IsNull(settings.ConnectionString);
	}

	[TestMethod]
	public void DataProtectionSettings_RemotePayload_RoundTripsResolvedConnectionString()
	{
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys",
			ConnectionString = "redis:6379,password=runtime-secret"
		};

		var json = JsonSerializer.Serialize(settings);
		var deserialized = JsonSerializer.Deserialize<DataProtectionSettings>(json);

		Assert.Contains("runtime-secret", json, System.StringComparison.Ordinal);
		Assert.IsNotNull(deserialized);
		Assert.AreEqual(settings.ConnectionString, deserialized.ConnectionString);
	}

	#endregion

	#region Validation Attributes Tests

	[TestMethod]
	public void ApplicationName_Required_ValidationFails_WhenNull()
	{
		// Arrange - can't test null for required properties directly
		// Instead verify the attribute exists
		var property = typeof(DataProtectionSettings).GetProperty(nameof(DataProtectionSettings.ApplicationName));
		var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), true);

		// Assert
		Assert.IsNotNull(requiredAttribute);
		Assert.IsTrue(requiredAttribute.Length > 0, "ApplicationName should have Required attribute");
	}

	[TestMethod]
	public void ConnectionStringKey_Required_ValidationFails_WhenNull()
	{
		// Arrange
		var property = typeof(DataProtectionSettings).GetProperty(nameof(DataProtectionSettings.ConnectionStringKey));
		var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), true);

		// Assert
		Assert.IsNotNull(requiredAttribute);
		Assert.IsTrue(requiredAttribute.Length > 0, "ConnectionStringKey should have Required attribute");
	}

	[TestMethod]
	public void CacheKey_Required_ValidationFails_WhenNull()
	{
		// Arrange
		var property = typeof(DataProtectionSettings).GetProperty(nameof(DataProtectionSettings.CacheKey));
		var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), true);

		// Assert
		Assert.IsNotNull(requiredAttribute);
		Assert.IsTrue(requiredAttribute.Length > 0, "CacheKey should have Required attribute");
	}

	[TestMethod]
	public void DefaultKeyLifetime_HasRangeAttribute()
	{
		// Arrange
		var property = typeof(DataProtectionSettings).GetProperty(nameof(DataProtectionSettings.DefaultKeyLifetime));
		var rangeAttribute = property?.GetCustomAttributes(typeof(RangeAttribute), true)
			.Cast<RangeAttribute>()
			.FirstOrDefault();

		// Assert
		Assert.IsNotNull(rangeAttribute, "DefaultKeyLifetime should have Range attribute");
		Assert.AreEqual(1, rangeAttribute.Minimum);
		Assert.AreEqual(365, rangeAttribute.Maximum);
	}

	[TestMethod]
	[DataRow(0, false)]
	[DataRow(1, true)]
	[DataRow(90, true)]
	[DataRow(365, true)]
	[DataRow(366, false)]
	[DataRow(-1, false)]
	public void DefaultKeyLifetime_RangeValidation(int value, bool expectedValid)
	{
		// Arrange
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys",
			DefaultKeyLifetime = value
		};

		var context = new ValidationContext(settings) { MemberName = nameof(DataProtectionSettings.DefaultKeyLifetime) };
		var results = new List<ValidationResult>();

		// Act
		var isValid = Validator.TryValidateProperty(settings.DefaultKeyLifetime, context, results);

		// Assert
		Assert.AreEqual(expectedValid, isValid, $"DefaultKeyLifetime={value} should be {(expectedValid ? "valid" : "invalid")}");
	}

	#endregion

	#region Purposes Dictionary Tests

	[TestMethod]
	public void Purposes_CanAddEntries()
	{
		// Arrange
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys"
		};

		// Act
		settings.Purposes.Add("key1", "value1");
		settings.Purposes.Add("key2", "value2");

		// Assert
		Assert.AreEqual(2, settings.Purposes.Count);
		Assert.AreEqual("value1", settings.Purposes["key1"]);
		Assert.AreEqual("value2", settings.Purposes["key2"]);
	}

	[TestMethod]
	public void Purposes_CanClearEntries()
	{
		// Arrange
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys"
		};
		settings.Purposes.Add("key1", "value1");

		// Act
		settings.Purposes.Clear();

		// Assert
		Assert.AreEqual(0, settings.Purposes.Count);
	}

	#endregion
}

[TestClass]
public class DataProtectionClientSettingsTests
{
	#region ConfigurationSectionName Tests

	[TestMethod]
	public void ConfigurationSectionName_IsNotNullOrEmpty()
	{
		// Assert
		Assert.IsFalse(string.IsNullOrEmpty(DataProtectionClientSettings.ConfigurationSectionName));
	}

	#endregion

	#region Default Values Tests

	[TestMethod]
	public void ConnectionString_DefaultValue_IsNull()
	{
		// Arrange
		var settings = new DataProtectionClientSettings
		{
			ConnectionStringKey = "ConfigService",
			Endpoint = "/api/DataProtection"
		};

		// Assert
		Assert.IsNull(settings.ConnectionString);
	}

	#endregion

	#region Validation Attributes Tests

	[TestMethod]
	public void ConnectionStringKey_Required_HasAttribute()
	{
		// Arrange
		var property = typeof(DataProtectionClientSettings).GetProperty(nameof(DataProtectionClientSettings.ConnectionStringKey));
		var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), true);

		// Assert
		Assert.IsNotNull(requiredAttribute);
		Assert.IsTrue(requiredAttribute.Length > 0, "ConnectionStringKey should have Required attribute");
	}

	[TestMethod]
	public void Endpoint_Required_HasAttribute()
	{
		// Arrange
		var property = typeof(DataProtectionClientSettings).GetProperty(nameof(DataProtectionClientSettings.Endpoint));
		var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), true);

		// Assert
		Assert.IsNotNull(requiredAttribute);
		Assert.IsTrue(requiredAttribute.Length > 0, "Endpoint should have Required attribute");
	}

	#endregion
}
