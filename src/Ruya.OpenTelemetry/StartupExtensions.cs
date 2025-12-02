using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Ruya.Primitives;

namespace Ruya.OpenTelemetry;

public static class StartupExtensions
{
    /// <summary>
    /// Configures OpenTelemetry with comprehensive instrumentation.
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        RegisterServices(builder);

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var resourceBuilder = CreateResourceBuilder(builder);

        ConfigureLogging(builder, resourceBuilder);
        ConfigureMetrics(builder, resourceBuilder);
        ConfigureTracing(builder, resourceBuilder);
        ConfigureExporters(builder);

        return builder;
    }

    /// <summary>
    /// Adds HTTP body capture middleware. Call after UseRouting().
    /// </summary>
    public static IApplicationBuilder UseHttpBodyCapture(this IApplicationBuilder app)
    {
        return app.UseMiddleware<HttpBodyCaptureMiddleware>();
    }

    private static void RegisterServices<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<OpenTelemetrySettings>()
            .Configure<IConfiguration>((settings, config) =>
            {
                config.GetSection(OpenTelemetrySettings.ConfigurationSectionName).Bind(settings);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton<HttpBodyCapture>();
        builder.Services.AddSingleton<SqlStatementSanitizer>();
        builder.Services.AddMetrics();
    }

    private static ResourceBuilder CreateResourceBuilder<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var settings = builder.Configuration
            .GetSection(OpenTelemetrySettings.ConfigurationSectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        var serviceName = settings.Service.Name ?? Startup.AssemblyName;
        var serviceVersion = settings.Service.Version ?? Startup.AssemblyVersion;

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: Environment.MachineName,
                serviceNamespace: settings.Service.Namespace);

        var attributes = new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["host.name"] = Environment.MachineName,
            ["process.runtime.name"] = ".NET",
            ["process.runtime.version"] = Environment.Version.ToString()
        };

        foreach (var tag in settings.CustomTags)
        {
            attributes[tag.Key] = tag.Value;
        }

        resourceBuilder.AddAttributes(attributes);

        if (settings.Kubernetes.Enabled && EnvironmentDetector.IsRunningInKubernetes())
        {
            resourceBuilder.AddAttributes(EnvironmentDetector.DetectKubernetesAttributes());
        }

        if (settings.Kubernetes.DetectContainer && EnvironmentDetector.IsRunningInContainer())
        {
            resourceBuilder.AddAttributes(EnvironmentDetector.DetectContainerAttributes());
        }

        return resourceBuilder;
    }

    private static void ConfigureLogging<TBuilder>(TBuilder builder, ResourceBuilder resourceBuilder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
            options.SetResourceBuilder(resourceBuilder);
        });
    }

    private static void ConfigureMetrics<TBuilder>(TBuilder builder, ResourceBuilder resourceBuilder)
        where TBuilder : IHostApplicationBuilder
    {
        var settings = builder.Configuration
            .GetSection(OpenTelemetrySettings.ConfigurationSectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder);

                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddMeter(Startup.AssemblyName);

                if (settings.Meters.Count > 0)
                {
                    metrics.AddMeter(settings.Meters.ToArray());
                }

                metrics.AddPrometheusExporter();
            });
    }

    private static void ConfigureTracing<TBuilder>(TBuilder builder, ResourceBuilder resourceBuilder)
        where TBuilder : IHostApplicationBuilder
    {
        var settings = builder.Configuration
            .GetSection(OpenTelemetrySettings.ConfigurationSectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder)
                    .AddSource(Startup.AssemblyName)
                    .AddProcessor(new EnvironmentTagProcessor(builder.Environment.EnvironmentName));

                SamplerConfiguration.Configure(tracing, settings.Sampling, builder.Environment.IsDevelopment());
                SamplerConfiguration.ConfigureBatchProcessor(settings.BatchProcessor);

                TracingInstrumentation.ConfigureAspNetCore(tracing, settings.Http);
                TracingInstrumentation.ConfigureHttpClient(tracing, settings.Http);
                TracingInstrumentation.ConfigureSql(tracing, settings.Sql);

                tracing
                    .AddGrpcClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddRedisInstrumentation();
            });
    }

    private static void ConfigureExporters<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.Services.AddOpenTelemetry()
                .UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri(otlpEndpoint));
        }
    }
}
