using System;

using OpenTelemetry.Trace;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Configures OpenTelemetry sampling strategies.
/// </summary>
internal static class SamplerConfiguration
{
    public static void Configure(TracerProviderBuilder tracing, SamplingSettings settings, bool isDevelopment)
    {
        if (isDevelopment)
        {
            tracing.SetSampler(new AlwaysOnSampler());
            return;
        }

        Sampler sampler = settings.Type switch
        {
            SamplerType.AlwaysOn => new AlwaysOnSampler(),
            SamplerType.AlwaysOff => new AlwaysOffSampler(),
            SamplerType.TraceIdRatio => new TraceIdRatioBasedSampler(settings.Ratio),
            SamplerType.ParentBased => CreateParentBasedSampler(settings),
            _ => new ParentBasedSampler(new TraceIdRatioBasedSampler(settings.Ratio))
        };

        tracing.SetSampler(sampler);
    }

    private static Sampler CreateParentBasedSampler(SamplingSettings settings)
    {
        Sampler rootSampler = settings.ParentBasedRootSampler switch
        {
            SamplerType.AlwaysOn => new AlwaysOnSampler(),
            SamplerType.AlwaysOff => new AlwaysOffSampler(),
            SamplerType.TraceIdRatio => new TraceIdRatioBasedSampler(settings.Ratio),
            _ => new TraceIdRatioBasedSampler(settings.Ratio)
        };

        return new ParentBasedSampler(rootSampler);
    }

    public static void ConfigureBatchProcessor(BatchProcessorSettings settings)
    {
        Environment.SetEnvironmentVariable("OTEL_BSP_MAX_EXPORT_BATCH_SIZE", settings.MaxExportBatchSize.ToString());
        Environment.SetEnvironmentVariable("OTEL_BSP_MAX_QUEUE_SIZE", settings.MaxQueueSize.ToString());
        Environment.SetEnvironmentVariable("OTEL_BSP_SCHEDULE_DELAY", ((int)settings.ScheduledDelay.TotalMilliseconds).ToString());
        Environment.SetEnvironmentVariable("OTEL_BSP_EXPORT_TIMEOUT", ((int)settings.ExporterTimeout.TotalMilliseconds).ToString());
    }
}
