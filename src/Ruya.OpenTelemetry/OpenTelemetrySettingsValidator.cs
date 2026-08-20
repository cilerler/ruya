using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

namespace Ruya.OpenTelemetry;

internal sealed class OpenTelemetrySettingsValidator : IValidateOptions<OpenTelemetrySettings>
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly HashSet<string> ReservedResourceAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "deployment.environment",
        "host.name",
        "process.runtime.name",
        "process.runtime.version",
        "service.instance.id",
        "service.name",
        "service.namespace",
        "service.version",
        "telemetry.sdk.language",
        "telemetry.sdk.name",
        "telemetry.sdk.version"
    };

    public ValidateOptionsResult Validate(string? name, OpenTelemetrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Service is null || settings.Sampling is null || settings.BatchProcessor is null ||
            settings.Http is null || settings.Sql is null || settings.Kubernetes is null ||
            settings.ActivitySources is null || settings.Meters is null || settings.CustomTags is null)
        {
            return ValidateOptionsResult.Fail("OpenTelemetry nested settings collections and objects cannot be null.");
        }

        if (settings.ActivitySources.Any(string.IsNullOrWhiteSpace) ||
            settings.Meters.Any(string.IsNullOrWhiteSpace) ||
            settings.CustomTags.Any(tag => string.IsNullOrWhiteSpace(tag.Key) || string.IsNullOrWhiteSpace(tag.Value)))
        {
            return ValidateOptionsResult.Fail("OpenTelemetry activity source names, meter names, and custom resource tags must be nonblank.");
        }

        if (settings.CustomTags.Keys.Any(ReservedResourceAttributes.Contains))
        {
            return ValidateOptionsResult.Fail("OpenTelemetry custom tags cannot override deployment, service, host, runtime, or telemetry SDK resource identity.");
        }

        if (settings.Service.Name is not null && string.IsNullOrWhiteSpace(settings.Service.Name) ||
            settings.Service.Version is not null && string.IsNullOrWhiteSpace(settings.Service.Version) ||
            settings.Service.Namespace is not null && string.IsNullOrWhiteSpace(settings.Service.Namespace))
        {
            return ValidateOptionsResult.Fail("OpenTelemetry service identity values must be null or nonblank.");
        }

        if (!Enum.IsDefined(settings.Sampling.Type))
        {
            return ValidateOptionsResult.Fail("OpenTelemetry:Sampling:Type must be a defined sampler type.");
        }

        if (settings.Sampling.Type == SamplerType.ParentBased &&
            settings.Sampling.ParentBasedRootSampler is not (
                SamplerType.AlwaysOn or SamplerType.AlwaysOff or SamplerType.TraceIdRatio))
        {
            return ValidateOptionsResult.Fail(
                "OpenTelemetry:Sampling:ParentBasedRootSampler must be AlwaysOn, AlwaysOff, or TraceIdRatio when Type is ParentBased.");
        }

        if (!double.IsFinite(settings.Sampling.Ratio) || settings.Sampling.Ratio is < 0 or > 1)
        {
            return ValidateOptionsResult.Fail("OpenTelemetry:Sampling:Ratio must be a finite value from 0 through 1.");
        }

        if (settings.BatchProcessor.MaxExportBatchSize is <= 0 or > 10_000 ||
            settings.BatchProcessor.MaxQueueSize is <= 0 or > 100_000)
        {
            return ValidateOptionsResult.Fail("OpenTelemetry batch sizes must be within their supported ranges.");
        }

        if (settings.BatchProcessor.MaxExportBatchSize > settings.BatchProcessor.MaxQueueSize)
        {
            return ValidateOptionsResult.Fail("OpenTelemetry:BatchProcessor:MaxExportBatchSize cannot exceed MaxQueueSize.");
        }

        if (!IsValidMilliseconds(settings.BatchProcessor.ScheduledDelay) ||
            !IsValidMilliseconds(settings.BatchProcessor.ExporterTimeout))
        {
            return ValidateOptionsResult.Fail("OpenTelemetry batch delays and timeouts must be positive and no greater than Int32.MaxValue milliseconds.");
        }

        if (settings.Http.MaxBodySizeBytes is < 0 or > 1_048_576)
        {
            return ValidateOptionsResult.Fail("OpenTelemetry:Http:MaxBodySizeBytes must be from zero through 1048576.");
        }

        if ((settings.Http.CaptureRequestBody || settings.Http.CaptureResponseBody) &&
            settings.Http.MaxBodySizeBytes <= 0)
        {
            return ValidateOptionsResult.Fail("OpenTelemetry:Http:MaxBodySizeBytes must be greater than zero when body capture is enabled.");
        }

        if (settings.Sql.MaxStatementLength is < 0 or > 50_000)
        {
            return ValidateOptionsResult.Fail("OpenTelemetry:Sql:MaxStatementLength must be from zero through 50000.");
        }

        if (settings.Http.AllowedContentTypes is null || settings.Http.ExcludeUrlPatterns is null ||
            settings.Http.RedactedHeaders is null || settings.Http.RedactedJsonPaths is null ||
            settings.Sql.SensitivePatterns is null)
        {
            return ValidateOptionsResult.Fail("OpenTelemetry HTTP and SQL settings collections cannot be null.");
        }

        if (settings.Http.AllowedContentTypes.Any(contentType =>
                string.IsNullOrWhiteSpace(contentType) ||
                !contentType.Contains("json", StringComparison.OrdinalIgnoreCase)))
        {
            return ValidateOptionsResult.Fail("OpenTelemetry HTTP body capture supports JSON content types only.");
        }

        var regexPatterns = settings.Http.ExcludeUrlPatterns.Concat(settings.Sql.SensitivePatterns).ToArray();
        if (regexPatterns.Any(string.IsNullOrWhiteSpace))
        {
            return ValidateOptionsResult.Fail("OpenTelemetry URL exclusion and SQL sanitization patterns must be nonblank.");
        }

        foreach (var pattern in regexPatterns)
        {
            try
            {
                _ = new Regex(
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RegexTimeout);
            }
            catch (ArgumentException)
            {
                return ValidateOptionsResult.Fail($"OpenTelemetry contains an invalid regular expression: {pattern}");
            }
        }

        if (settings.Http.RedactedHeaders.Any(string.IsNullOrWhiteSpace) ||
            settings.Http.RedactedJsonPaths.Any(path =>
                string.IsNullOrWhiteSpace(path) ||
                !path.StartsWith("$.", StringComparison.Ordinal) ||
                path.Split('.').Any(string.IsNullOrWhiteSpace)))
        {
            return ValidateOptionsResult.Fail("OpenTelemetry redacted headers and JSON paths must be nonblank; JSON paths must use dot notation beginning with '$.'.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsValidMilliseconds(TimeSpan value) =>
        value > TimeSpan.Zero && value.TotalMilliseconds <= int.MaxValue;
}
