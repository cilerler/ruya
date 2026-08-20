using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Ruya.OpenTelemetry.Tests;

[TestClass]
public sealed class OpenTelemetrySafetyTests
{
    [TestMethod]
    public void Validate_BatchLargerThanQueue_ReturnsFailure()
    {
        var settings = new OpenTelemetrySettings();
        settings.BatchProcessor.MaxExportBatchSize = 3;
        settings.BatchProcessor.MaxQueueSize = 2;

        var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_NonJsonBodyContentType_ReturnsFailure()
    {
        var settings = new OpenTelemetrySettings();
        settings.Http.AllowedContentTypes.Add("text/plain");

        var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_NullNestedSettings_ReturnsFailure()
    {
        var settings = new OpenTelemetrySettings { Http = null! };

        var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_NullOrBlankInstrumentationName_ReturnsFailure()
    {
        OpenTelemetrySettings[] invalidSettings =
        [
            new() { ActivitySources = null! },
            new() { ActivitySources = [" "] },
            new() { Meters = null! },
            new() { Meters = [" "] }
        ];

        foreach (var settings in invalidSettings)
        {
            var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

            Assert.IsTrue(result.Failed);
        }
    }

    [TestMethod]
    public void Defaults_OptionalRuyaLibraryNamesAreNotOwnedByOpenTelemetryPackage()
    {
        var settings = new OpenTelemetrySettings();
        string[] optionalActivitySources =
        [
            "Ruya.Services.CloudStorage.Amazon",
            "Ruya.Services.CloudStorage.Azure",
            "Ruya.Services.DistributedLock"
        ];
        string[] optionalMeters =
        [
            "Ruya.Diagnostics.DistributedTracing",
            .. optionalActivitySources
        ];

        foreach (var sourceName in optionalActivitySources)
        {
            CollectionAssert.DoesNotContain(settings.ActivitySources, sourceName);
        }

        foreach (var meterName in optionalMeters)
        {
            CollectionAssert.DoesNotContain(settings.Meters, meterName);
        }
    }

    [TestMethod]
    public void Validate_BlankExclusionPattern_ReturnsFailure()
    {
        var settings = new OpenTelemetrySettings();
        settings.Http.ExcludeUrlPatterns = [" "];

        var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_ReservedEnvironmentTag_ReturnsFailure()
    {
        var settings = new OpenTelemetrySettings();
        settings.CustomTags["deployment.environment"] = "spoofed";

        var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_UndefinedSamplerType_ReturnsFailure()
    {
        var settings = new OpenTelemetrySettings();
        settings.Sampling.Type = (SamplerType)int.MaxValue;

        var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

        Assert.IsTrue(result.Failed);
    }

    [TestMethod]
    public void Validate_ParentBasedWithUnsupportedRootSampler_ReturnsFailure()
    {
        SamplerType[] unsupportedRootSamplers =
        [
            SamplerType.ParentBased,
            (SamplerType)int.MaxValue
        ];

        foreach (var rootSampler in unsupportedRootSamplers)
        {
            var settings = new OpenTelemetrySettings();
            settings.Sampling.Type = SamplerType.ParentBased;
            settings.Sampling.ParentBasedRootSampler = rootSampler;

            var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

            Assert.IsTrue(result.Failed);
        }
    }

    [TestMethod]
    public void Validate_NestedValueOutsideSupportedRange_ReturnsFailure()
    {
        Action<OpenTelemetrySettings>[] invalidMutations =
        [
            settings => settings.BatchProcessor.MaxExportBatchSize = 10_001,
            settings => settings.BatchProcessor.MaxQueueSize = 100_001,
            settings => settings.Http.MaxBodySizeBytes = 1_048_577,
            settings => settings.Sql.MaxStatementLength = -1,
            settings => settings.Sql.MaxStatementLength = 50_001
        ];

        foreach (var mutate in invalidMutations)
        {
            var settings = new OpenTelemetrySettings();
            mutate(settings);

            var result = new OpenTelemetrySettingsValidator().Validate(null, settings);

            Assert.IsTrue(result.Failed);
        }
    }

    [TestMethod]
    public void Configure_DevelopmentAlwaysOff_DropsRootSpan()
    {
        const string sourceName =
            $"{nameof(Ruya)}.{nameof(Ruya.OpenTelemetry)}.{nameof(Ruya.OpenTelemetry.Tests)}.Sampling";
        var exported = new List<Activity>();
        var builder = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddInMemoryExporter(exported);
        SamplerConfiguration.Configure(
            builder,
            new SamplingSettings { Type = SamplerType.AlwaysOff },
            isDevelopment: true);

        using var provider = builder.Build();
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("dropped");
        provider.ForceFlush();

        Assert.IsNotNull(activity);
        Assert.IsFalse(activity.Recorded);
        Assert.HasCount(0, exported);
    }

    [TestMethod]
    public async Task CaptureRequestBodyAsync_ChunkedOversizedJson_ReturnsMarkerAndRewindsBody()
    {
        var capture = CreateCapture(maxBodySizeBytes: 16);
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"value\":\"this-is-too-large\"}"));

        var result = await capture.CaptureRequestBodyAsync(context.Request);

        Assert.AreEqual(HttpBodyCapture.BodyTooLarge, result);
        Assert.AreEqual(0, context.Request.Body.Position);
    }

    [TestMethod]
    public async Task CaptureRequestBodyAsync_JsonSecret_RedactsConfiguredValue()
    {
        var capture = CreateCapture(maxBodySizeBytes: 128);
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"password\":\"do-not-export\",\"name\":\"test\"}"));

        var result = await capture.CaptureRequestBodyAsync(context.Request);

        Assert.IsNotNull(result);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-export", result, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CaptureRequestBodyAsync_CallerCancelsInFlight_ThrowsOperationCanceledException()
    {
        var capture = CreateCapture(maxBodySizeBytes: 128);
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new CancellableReadStream();
        using var cancellationTokenSource = new CancellationTokenSource();

        var captureTask = capture.CaptureRequestBodyAsync(context.Request, cancellationTokenSource.Token);
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => captureTask);
    }

    [TestMethod]
    public void SanitizeBody_InvalidJson_ReturnsMarkerWithoutRawBody()
    {
        var capture = CreateCapture(maxBodySizeBytes: 128);

        var result = capture.SanitizeBody("{secret-content", "application/json");

        Assert.AreEqual(HttpBodyCapture.InvalidJson, result);
        Assert.DoesNotContain("secret-content", result!, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CaptureRequestBodyAsync_DuplicateJsonProperty_ReturnsInvalidMarkerAndRewindsBody()
    {
        const string body = "{\"password\":\"first\",\"password\":\"secret\"}";
        var capture = CreateCapture(maxBodySizeBytes: 128);
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        var result = await capture.CaptureRequestBodyAsync(context.Request);

        Assert.AreEqual(HttpBodyCapture.InvalidJson, result);
        Assert.AreEqual(0, context.Request.Body.Position);
        Assert.DoesNotContain("secret", result!, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CaptureResponseBodyAsync_DuplicateJsonProperty_ReturnsInvalidMarkerAndForwardsResponse()
    {
        const string body = "{\"password\":\"first\",\"password\":\"secret\"}";
        var capture = CreateCapture(maxBodySizeBytes: 128);
        var context = new DefaultHttpContext();
        context.Response.ContentType = "application/json";
        context.Response.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        await using var destination = new MemoryStream();

        var result = await capture.CaptureResponseBodyAsync(context.Response, destination);

        Assert.AreEqual(HttpBodyCapture.InvalidJson, result);
        Assert.AreEqual(body, Encoding.UTF8.GetString(destination.ToArray()));
        Assert.DoesNotContain("secret", result!, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SanitizeBody_MultibyteJsonExceedsByteLimit_ReturnsBodyTooLarge()
    {
        var capture = CreateCapture(maxBodySizeBytes: 12);

        var result = capture.SanitizeBody("{\"v\":\"😀😀\"}", "application/json");

        Assert.AreEqual(HttpBodyCapture.BodyTooLarge, result);
    }

    [TestMethod]
    public void Sanitize_SensitiveLiteralCrossesTruncationBoundary_DoesNotExportSecret()
    {
        var settings = new OpenTelemetrySettings();
        settings.Sql.MaxStatementLength = 48;
        var sanitizer = new SqlStatementSanitizer(Options.Create(settings));
        var secret = new string('s', 128);

        var result = sanitizer.Sanitize($"SELECT * FROM Users WHERE password='{secret}' AND Active=1");

        Assert.DoesNotContain(secret, result, StringComparison.Ordinal);
        Assert.DoesNotContain("password='", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
        Assert.Contains("[TRUNCATED]", result, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Sanitize_NumericHexAndDecimalLiterals_RedactsEveryValue()
    {
        var sanitizer = new SqlStatementSanitizer(Options.Create(new OpenTelemetrySettings()));

        var result = sanitizer.Sanitize(
            "SELECT * FROM Customers WHERE ssn=123456789 AND balance=42.75 AND token=0xDEADBEEF AND active=1;");

        Assert.DoesNotContain("123456789", result, StringComparison.Ordinal);
        Assert.DoesNotContain("42.75", result, StringComparison.Ordinal);
        Assert.DoesNotContain("DEADBEEF", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("active=1", result, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void EnrichSqlCommand_TextCommand_EmitsCanonicalAndCompatibilityTagsWithSanitizedValue()
    {
        var settings = new OpenTelemetrySettings().Sql;
        var sanitizer = new SqlStatementSanitizer(settings);
        using var command = new SqlCommand("SELECT * FROM Customers WHERE ssn=123456789");
        using var activity = new Activity("sql-test");
        activity.Start();

        TracingInstrumentation.EnrichSqlCommand(activity, command, settings, sanitizer);

        var canonical = activity.GetTagItem("db.query.text")?.ToString();
        Assert.IsNotNull(canonical);
        Assert.DoesNotContain("123456789", canonical, StringComparison.Ordinal);
        Assert.AreEqual(canonical, activity.GetTagItem("db.statement"));
    }

    [TestMethod]
    public void Sanitize_ConfiguredPatternTimesOut_ReturnsFailClosedMarker()
    {
        var settings = new OpenTelemetrySettings();
        settings.Sql.SensitivePatterns = ["^(a+)+$"];
        var sanitizer = new SqlStatementSanitizer(Options.Create(settings));

        var result = sanitizer.Sanitize(new string('a', 32_768) + "!");

        Assert.AreEqual(SqlStatementSanitizer.RegexTimeoutMarker, result);
    }

    [TestMethod]
    public void IsUrlExcluded_AnchoredPattern_UsesRegexSemantics()
    {
        var settings = new OpenTelemetrySettings();
        settings.Http.ExcludeUrlPatterns = ["^/private/"];
        var capture = new HttpBodyCapture(
            Options.Create(settings),
            NullLogger<HttpBodyCapture>.Instance);

        Assert.IsTrue(capture.IsUrlExcluded("/private/orders"));
        Assert.IsFalse(capture.IsUrlExcluded("/public/private/orders"));
    }

    [TestMethod]
    public async Task WriteAsync_ResponseLargerThanCaptureLimit_ForwardsCompleteResponseAndBoundsTelemetry()
    {
        var capture = CreateCapture(maxBodySizeBytes: 8);
        await using var destination = new MemoryStream();
        await using var stream = new BoundedCaptureStream(destination, capture.MaxBodySizeBytes);
        var response = Encoding.UTF8.GetBytes("{\"message\":\"a response much larger than the telemetry limit\"}");

        await stream.WriteAsync(response);
        var result = stream.GetCapturedBody(capture, "application/json");

        Assert.AreEqual(response.Length, destination.Length);
        Assert.AreEqual(HttpBodyCapture.BodyTooLarge, result);
    }

    [TestMethod]
    public void Constructor_ReleasedMiddlewareSignature_RemainsAvailable()
    {
        var constructor = typeof(HttpBodyCaptureMiddleware).GetConstructor(
            [typeof(RequestDelegate), typeof(IOptions<OpenTelemetrySettings>)]);

        Assert.IsNotNull(constructor);
    }

    private static HttpBodyCapture CreateCapture(int maxBodySizeBytes)
    {
        var settings = new OpenTelemetrySettings();
        settings.Http.CaptureRequestBody = true;
        settings.Http.CaptureResponseBody = true;
        settings.Http.MaxBodySizeBytes = maxBodySizeBytes;

        return new HttpBodyCapture(
            Options.Create(settings),
            NullLogger<HttpBodyCapture>.Instance);
    }

    private sealed class CancellableReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
