using System;
using Microsoft.Extensions.Configuration;

namespace Ruya.Extensions.Configuration;

public static partial class StartupExtensions
{
	public const string ConfigurationSectionName = "EnvironmentVariablesPrefix";

	public static IConfigurationBuilder AddEnvironmentVariablesWithPrefix(this IConfigurationManager configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var prefix = configuration.GetValue<string>(ConfigurationSectionName);
		if (!string.IsNullOrWhiteSpace(prefix))
		{
			configuration.AddEnvironmentVariables(prefix);
		}

		return configuration;
	}
}
