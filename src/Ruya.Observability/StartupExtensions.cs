using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Ruya.Observability.Middlewares;
using Ruya.Observability.Processors;

namespace Ruya.Observability;

public static class StartupExtensions
{
	/// <summary>
	///     Adds OpenTelemetryTracing and OpenTelemetryMetrics to project
	/// </summary>
	/// <param name="configureTracing">
	///     Callback for custom tracing configuration. HttpClientInstrumentation, DefaultSource,
	///     ResourceBuild and JaegerExporter are added by default
	/// </param>
	/// <param name="configureMetrics">
	///     Callback for custom metrics configuration. HttpClientInstrumentation, RuntimeMetrics and
	///     PrometheusExporter are added by default
	/// </param>
	public static void AddDistributedTracingAndMetrics(
		this IServiceCollection serviceCollection,
		IConfiguration configuration,
		Action<TracerProviderBuilder>? configureTracing = null,
		Action<MeterProviderBuilder>? configureMetrics = null)
	{
		serviceCollection.AddSingleton<TraceManager>();
		string name = Assembly.GetEntryAssembly()?.GetName().Name ?? TracingSetting.DefaultName;
		string version = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? TracingSetting.DefaultVersion;
		ResourceBuilder resourceBuilder = ResourceBuilder.CreateDefault()
			.AddService(
				name,
				serviceVersion: version,
				serviceInstanceId: Environment.MachineName);

		Activity.DefaultIdFormat = ActivityIdFormat.W3C;
		Activity.ForceDefaultIdFormat = true;

		var setting = new TracingSetting();
		IConfigurationSection? settingSection = configuration.GetSection(TracingSetting.ConfigurationSectionName);
		settingSection.Bind(setting);
		serviceCollection.Configure<TracingSetting>(settingSection);
		string connectionString = configuration.GetConnectionString(setting.ConnectionStringKey);
		serviceCollection.AddOpenTelemetryTracing(options =>
		{
			options.SetResourceBuilder(resourceBuilder)
				.AddHttpClientInstrumentation(options =>
				{
					options.RecordException = true;
				})
				.AddProcessor(new EnvironmentTagProcessor())
				.AddSource(DistributedTracing.AssemblyName);

			if (connectionString != null)
				options.AddOtlpExporter(options =>
				{
					options.Endpoint = new Uri(connectionString);
					options.Protocol = OtlpExportProtocol.HttpProtobuf;
				});
			configureTracing?.Invoke(options);
		});

		serviceCollection.AddOpenTelemetryMetrics(options =>
		{
			options.SetResourceBuilder(resourceBuilder)
				.AddHttpClientInstrumentation()
				.AddRuntimeInstrumentation()
				.AddPrometheusExporter()
				.AddMeter(DistributedTracing.AssemblyName);
			configureMetrics?.Invoke(options);
		});
	}

	public static MeterProvider BuildMeterProvider(this IHost host, Action<MeterProviderBuilder>? configureMeters = null)
	{
		IConfiguration configuration = host.Services.GetRequiredService<IConfiguration>();
		MeterProviderBuilder builder = Sdk.CreateMeterProviderBuilder()
			.AddRuntimeInstrumentation()
			.AddHttpClientInstrumentation()
			.AddMeter(DistributedTracing.AssemblyName)
			.AddPrometheusExporter(options =>
			{
				string endpoint = configuration.GetValue<string>("PrometheusExporterHttpListener");
				options.StartHttpListener = true;

				// https://github.com/open-telemetry/opentelemetry-dotnet/issues/2840
				// options.HttpListenerPrefixes = new[] { endpoint };
				options.GetType()
					?.GetField("httpListenerPrefixes", BindingFlags.NonPublic | BindingFlags.Instance)
					?.SetValue(options, new[] { endpoint });
				options.ScrapeResponseCacheDurationMilliseconds = 0;
			});

		configureMeters?.Invoke(builder);
		return builder.Build();
	}

	public static void UseRequestMetrics(this IApplicationBuilder builder)
	{
		builder.UseMiddleware<RequestMetricsMiddleware>();
	}
}
