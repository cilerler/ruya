using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Ruya.Testing.Primitives;

[SuppressMessage("Naming", "CA1724", Justification = "The released TestHost type name is retained for 8.x binary compatibility.")]
public static class TestHost
{
	public const string DefaultEnvironmentVariablePrefix = "RUYA_TEST_";
	private static readonly object SyncRoot = new();
	private static IServiceProvider? _rootServiceProvider;

	public static IServiceProvider? RootServiceProvider
	{
		get => Volatile.Read(ref _rootServiceProvider);
		private set => Volatile.Write(ref _rootServiceProvider, value);
	}

	public static void Initialize(Action<IServiceCollection, IConfiguration>? customConfig = null)
		=> Initialize(customConfig, DefaultEnvironmentVariablePrefix);

	public static void Initialize(
		Action<IServiceCollection, IConfiguration>? customConfig,
		string environmentVariablePrefix)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariablePrefix);

		lock (SyncRoot)
		{
			if (_rootServiceProvider is not null)
			{
				throw new InvalidOperationException("TestHost is already initialized. Call Cleanup before initializing it again.");
			}

			var defaultConfiguration = new Dictionary<string, string?>
			{
				["ConnectionStrings:TestServer"] = "http://test.local:80"
			};

			var configuration = new ConfigurationBuilder()
				.AddInMemoryCollection(defaultConfiguration)
				.SetBasePath(AppContext.BaseDirectory)
				.AddJsonFile("appsettings.Test.json", optional: true)
				.AddEnvironmentVariables(environmentVariablePrefix)
				.Build();

			try
			{
				var serviceCollection = new ServiceCollection();
				serviceCollection.AddSingleton<IConfiguration>(_ => configuration);
				serviceCollection.AddOptions();
				serviceCollection.AddLogging(loggingBuilder =>
				{
					loggingBuilder.SetMinimumLevel(LogLevel.Information)
						.AddFilter("System", LogLevel.Warning)
						.AddFilter("Microsoft", LogLevel.Warning);
					var loggingSection = configuration.GetSection("Logging");
					if (loggingSection.Exists())
					{
						loggingBuilder.AddConfiguration(loggingSection);
					}
				});

				customConfig?.Invoke(serviceCollection, configuration);

				RootServiceProvider = serviceCollection.BuildServiceProvider(new ServiceProviderOptions
				{
					ValidateOnBuild = true,
					ValidateScopes = true
				});
			}
			catch
			{
				(configuration as IDisposable)?.Dispose();
				throw;
			}
		}
	}

	public static void Cleanup()
	{
		IServiceProvider? provider;
		lock (SyncRoot)
		{
			provider = RootServiceProvider;
			RootServiceProvider = null;
		}

		(provider as IDisposable)?.Dispose();
	}

	public static async ValueTask CleanupAsync()
	{
		IServiceProvider? provider;
		lock (SyncRoot)
		{
			provider = RootServiceProvider;
			RootServiceProvider = null;
		}

		if (provider is IAsyncDisposable asyncDisposable)
		{
			await asyncDisposable.DisposeAsync().ConfigureAwait(false);
		}
		else
		{
			(provider as IDisposable)?.Dispose();
		}
	}
}
