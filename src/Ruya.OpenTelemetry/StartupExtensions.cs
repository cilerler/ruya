using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        ArgumentNullException.ThrowIfNull(builder);

        var settings = GetValidatedSettings(builder.Configuration);
        RegisterServices(builder);

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var resourceBuilder = CreateResourceBuilder(builder, settings);

        var otlpEndpoint = GetOtlpEndpoint(builder.Configuration);
        ConfigureLogging(builder, resourceBuilder, otlpEndpoint);
        ConfigureMetrics(builder, resourceBuilder, otlpEndpoint, settings);
        ConfigureTracing(builder, resourceBuilder, otlpEndpoint, settings);

        return builder;
    }

    /// <summary>
    /// Adds HTTP body capture middleware. Call after UseRouting().
    /// </summary>
    public static IApplicationBuilder UseHttpBodyCapture(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<HttpBodyCaptureMiddleware>();
    }

    private static void RegisterServices<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<OpenTelemetrySettings>()
            .BindConfiguration(OpenTelemetrySettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OpenTelemetrySettings>, OpenTelemetrySettingsValidator>());

        builder.Services.AddSingleton<HttpBodyCapture>();
        builder.Services.AddSingleton<SqlStatementSanitizer>();
        builder.Services.AddMetrics();
    }

    private static ResourceBuilder CreateResourceBuilder<TBuilder>(
        TBuilder builder,
        OpenTelemetrySettings settings)
        where TBuilder : IHostApplicationBuilder
    {
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
            ["process.runtime.name"] = ".NET",
            ["process.runtime.version"] = Environment.Version.ToString()
        };

        if (settings.Kubernetes.DetectHost)
        {
            attributes["host.name"] = Environment.MachineName;
        }

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

    private static void ConfigureLogging<TBuilder>(
        TBuilder builder,
        ResourceBuilder resourceBuilder,
        Uri? otlpEndpoint)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
            options.SetResourceBuilder(resourceBuilder);

            if (otlpEndpoint is not null)
            {
                options.AddOtlpExporter(exporter => ConfigureOtlpExporter(exporter, otlpEndpoint));
            }
        });
    }

    private static void ConfigureMetrics<TBuilder>(
        TBuilder builder,
        ResourceBuilder resourceBuilder,
        Uri? otlpEndpoint,
        OpenTelemetrySettings settings)
        where TBuilder : IHostApplicationBuilder
    {
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

                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(exporter => ConfigureOtlpExporter(exporter, otlpEndpoint));
                }
            });
    }

    private static void ConfigureTracing<TBuilder>(
        TBuilder builder,
        ResourceBuilder resourceBuilder,
        Uri? otlpEndpoint,
        OpenTelemetrySettings settings)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder)
                    .AddSource(Startup.AssemblyName)
                    .AddProcessor(new EnvironmentTagProcessor(builder.Environment.EnvironmentName));

                if (settings.ActivitySources.Count > 0)
                {
                    tracing.AddSource(settings.ActivitySources.ToArray());
                }

                SamplerConfiguration.Configure(tracing, settings.Sampling, builder.Environment.IsDevelopment());

                TracingInstrumentation.ConfigureAspNetCore(tracing, settings.Http);
                TracingInstrumentation.ConfigureHttpClient(tracing, settings.Http);
                TracingInstrumentation.ConfigureSql(tracing, settings.Sql);

                tracing
                    .AddGrpcClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddRedisInstrumentation();

                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(exporter =>
                    {
                        ConfigureOtlpExporter(exporter, otlpEndpoint);
                        exporter.BatchExportProcessorOptions.MaxExportBatchSize = settings.BatchProcessor.MaxExportBatchSize;
                        exporter.BatchExportProcessorOptions.MaxQueueSize = settings.BatchProcessor.MaxQueueSize;
                        exporter.BatchExportProcessorOptions.ScheduledDelayMilliseconds =
                            checked((int)settings.BatchProcessor.ScheduledDelay.TotalMilliseconds);
                        exporter.BatchExportProcessorOptions.ExporterTimeoutMilliseconds =
                            checked((int)settings.BatchProcessor.ExporterTimeout.TotalMilliseconds);
                    });
                }
            });
    }

    private static Uri? GetOtlpEndpoint(IConfiguration configuration)
    {
        var configuredEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
        {
            return null;
        }

        return Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var endpoint) &&
            (endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                ? endpoint
                : throw new InvalidOperationException("OTEL_EXPORTER_OTLP_ENDPOINT must be an absolute HTTP or HTTPS URI.");
    }

    private static void ConfigureOtlpExporter(OtlpExporterOptions exporter, Uri endpoint)
    {
        exporter.Protocol = OtlpExportProtocol.Grpc;
        exporter.Endpoint = endpoint;
    }

    private static OpenTelemetrySettings GetValidatedSettings(IConfiguration configuration)
    {
        var settings = configuration
            .GetSection(OpenTelemetrySettings.ConfigurationSectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();
        var validation = new OpenTelemetrySettingsValidator().Validate(null, settings);

        if (validation.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(OpenTelemetrySettings),
                validation.Failures);
        }

        return settings;
    }
}
