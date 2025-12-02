using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Ruya.Testing.Primitives;

public static class TestHost
{
	// Holds the global container
	public static IServiceProvider? RootServiceProvider { get; private set; }

	public static void Initialize(Action<IServiceCollection, IConfiguration>? customConfig = null)
	{
        var myConfiguration = new Dictionary<string, string?>
        {
            {"ConnectionStrings::TestServer", "http://test.local:80"}
        };

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(myConfiguration)
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables()
			.Build();

		var serviceCollection = new ServiceCollection();
		serviceCollection.AddSingleton<IConfiguration>(configuration);
		serviceCollection.AddOptions();
		serviceCollection.AddLogging(loggingBuilder =>
			{
				loggingBuilder.SetMinimumLevel(LogLevel.Information)
				.AddFilter("System", LogLevel.Warning)
				.AddFilter("Microsoft", LogLevel.Warning);
				IConfigurationSection loggingSection = configuration.GetSection("Logging");
				if (loggingSection.Exists())
				{
					loggingBuilder.AddConfiguration(loggingSection);
				}
	#pragma warning disable S125 // Sections of code should not be commented out
				//loggingBuilder.AddDebug();
				//loggingBuilder.AddConsole(options => options.IncludeScopes = true)
	#pragma warning restore S125 // Sections of code should not be commented out
			});

		customConfig?.Invoke(serviceCollection, configuration);

		RootServiceProvider = serviceCollection.BuildServiceProvider();
	}

	public static void Cleanup()
	{
		(RootServiceProvider as IDisposable)?.Dispose();
	}
}
