using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Services.CloudStorage.Google.Tests;

[TestClass]
public class Initialize
{
	private static ILogger _logger;
	private static TestContext _testContext;
	public static IServiceCollection ServiceCollection { get; private set; }
	public static IServiceProvider ServiceProvider { get; private set; }

	[AssemblyInitialize]
	public static void AssemblyInitialize(TestContext testContext)
	{
		_testContext = testContext;
		IConfigurationRoot configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.Test.json"
				, true
				, true)
			.Build();


		ServiceCollection = new ServiceCollection();
		ServiceCollection.AddSingleton(configuration);
		ServiceCollection.AddSingleton<IConfiguration>(configuration);
		ServiceCollection.AddOptions();
		ServiceCollection.AddLogging(loggingBuilder =>
		{
			IConfigurationSection loggingSection = configuration.GetSection("Logging");
			if (loggingSection.Exists())
			{
				loggingBuilder.AddConfiguration(loggingSection);
			}
		});

		ServiceProvider = ServiceCollection.BuildServiceProvider();
	}

	[AssemblyCleanup]
	public static void AssemblyCleanup()
	{
		_logger.LogInformation("Cleaning up the assembly...");

		Thread.Sleep(TimeSpan.FromSeconds(5));
	}

	[ClassInitialize]
	public static void ClassInitialize(TestContext testContext)
	{
		_logger = ServiceProvider.GetRequiredService<ILogger<Initialize>>();
		_logger.LogInformation("Initializing a test class...");
	}

	[ClassCleanup]
	public static void ClassCleanup()
	{
		_logger.LogInformation("Cleaning up a test class...");
	}

	[TestInitialize]
	public void TestInitialize()
	{
		_logger.LogInformation("Initializing a test...");
	}

	[TestCleanup]
	public void TestCleanup()
	{
		_logger.LogInformation("Cleaning up a test...");
	}

	[Priority(1)]
	[TestCategory("Guards")]
	[TestMethod]
	public void IsServiceProviderNull()
	{
		_logger.LogInformation("Running a test...");
		Assert.IsNotNull(ServiceProvider);
	}

	[Priority(1)]
	[TestCategory("Guards")]
	[TestMethod]
	public void IsLoggerNull()
	{
		_logger.LogInformation("Running a test...");
		Assert.IsNotNull(_logger);
	}
}
