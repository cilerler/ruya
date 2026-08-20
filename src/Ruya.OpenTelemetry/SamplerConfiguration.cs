using OpenTelemetry.Trace;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Configures OpenTelemetry sampling strategies.
/// </summary>
internal static class SamplerConfiguration
{
    public static void Configure(TracerProviderBuilder tracing, SamplingSettings settings, bool isDevelopment)
    {
        _ = isDevelopment; // Retained in the internal signature for compatibility; configuration always wins.

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

    private static ParentBasedSampler CreateParentBasedSampler(SamplingSettings settings)
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
}
