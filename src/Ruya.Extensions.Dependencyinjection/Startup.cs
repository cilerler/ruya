using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Ruya.Primitives;

namespace Ruya.Extensions.Dependencyinjection
{
	public class Startup
	{
		public IConfigurationRoot Configuration { get; private set; }
		public IServiceProvider ServiceProvider { get; private set; }

		private void Register()
		{
			BeforeRegistrations();
			RegisterConfiguration();
			RegisterServices();
			AfterRegistrations();
		}

		private static void BeforeRegistrations()
		{
			#region directory
			string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			if (assemblyDirectory != null)
			{
				Directory.SetCurrentDirectory(assemblyDirectory);
			}
			#endregion
		}

		private void RegisterConfiguration()
		{
            string environmentName = StartupInjector.Instance.EnvironmentName;

			Dictionary<string, string> config = null;
			if (StartupInjector.Instance.InMemoryCollectionExist)
			{
				config = StartupInjector.Instance.RegisterInMemoryCollection();
			}

			var configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory());
			if (config != null)
				configuration = configuration.AddInMemoryCollection(config);

			configuration.AddJsonFile("appsettings.json", true, true)
						 .AddJsonFile($"appsettings.{environmentName}.json", true, true)
						 //.AddUserSecrets<Startup>()
						 .AddEnvironmentVariables();
                         //.AddCommandLine(args)
			Configuration = configuration.Build();
		}

		private void RegisterServices()
		{
			IServiceCollection serviceCollection = new ServiceCollection();

			serviceCollection.AddSingleton(Configuration);
			serviceCollection.AddSingleton<IConfiguration>(Configuration);

			serviceCollection.AddOptions();
			serviceCollection.AddLogging(ConfigureLogging);

			//x serviceCollection.AddMemoryCache();
			//X serviceCollection.AddDistributedMemoryCache();

			//serviceCollection.AddDistributedSqlServerCache(options =>
			//										  {
			//											  options.ConnectionString = Configuration.GetConnectionString(Configuration.GetValue<string>("Cache:SqlServer:ConnectionStringKey"));
			//											  options.SchemaName = Configuration.GetValue<string>("Cache:SqlServer:SchemaName");
			//											  options.TableName = Configuration.GetValue<string>("Cache:SqlServer:TableName");
			//										  });

			//serviceCollection.AddDistributedRedisCache(options =>
			//                                  {
			//	                                  options.Configuration = Configuration.GetConnectionString(Configuration.GetValue<string>("Cache:Redis:Configuration"));
			//	                                  options.InstanceName = Configuration.GetValue<string>("Cache:Redis:InstanceName");
			//	                                  Configuration.GetSection("ConnectionStrings:RedisConnection").Bind(options);
			//	                                  //x options.ResolveDns();
			//                                  });

			if (StartupInjector.Instance.ExternalServicesExist)
			{
				StartupInjector.Instance.RegisterExternalServices(serviceCollection, Configuration);
			}

			ServiceProvider = serviceCollection.BuildServiceProvider();
		}

		private void ConfigureLogging(ILoggingBuilder loggingBuilder)
		{
			loggingBuilder.AddConfiguration(Configuration.GetSection("Logging"))
						  .AddSerilog(dispose: true);
			//x loggingBuilder.AddDebug();
			//x loggingBuilder.AddConsole();

			if (StartupInjector.Instance.ExternalLoggingBuilder)
			{
				StartupInjector.Instance.RegisterExternalLoggingBuilder(loggingBuilder);
			}

		}

		private void AfterRegistrations()
		{
		}

		#region Singleton

		private static readonly Lazy<Startup> Lazy = new Lazy<Startup>(() => new Startup());

		public static Startup Instance => Lazy.Value;

		private Startup()
		{
			Register();
		}

		#endregion
	}
}
