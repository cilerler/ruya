# Ruya.OpenTelemetry

Configures logs, metrics, traces, resource identity, Prometheus scraping, and optional OTLP export for a deployable .NET process.

## Registration

```csharp
builder.ConfigureOpenTelemetry();

var app = builder.Build();
app.MapPrometheusScrapingEndpoint();
```

The configured sampling policy is used in every environment. Development does not silently switch to 100% sampling.

## Configuration

```json
{
  "OpenTelemetry": {
    "Service": {
      "Name": "MyOrganization.MyProduct.Host",
      "Version": "1.0.0",
      "Namespace": "MyOrganization.MyProduct.Host"
    },
    "Sampling": {
      "Type": "ParentBased",
      "ParentBasedRootSampler": "TraceIdRatio",
      "Ratio": 0.1
    },
    "BatchProcessor": {
      "MaxExportBatchSize": 512,
      "MaxQueueSize": 2048,
      "ScheduledDelay": "00:00:05",
      "ExporterTimeout": "00:00:30"
    },
    "ActivitySources": [ "MyOrganization.MyProduct.Library" ],
    "Meters": [ "MyOrganization.MyProduct.Library" ],
    "Http": {
      "CaptureRequestBody": false,
      "CaptureResponseBody": false,
      "MaxBodySizeBytes": 32768,
      "AllowedContentTypes": [ "application/json" ]
    },
    "Sql": {
      "RecordException": true,
      "CaptureCommandText": true,
      "SanitizeStatements": true
    }
  }
}
```

Supply the optional collector endpoint through deployment configuration:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
```

The endpoint must be an absolute HTTP or HTTPS URI. Batch settings are applied through the OpenTelemetry options API; the library does not mutate process-wide `OTEL_BSP_*` environment variables.

## Application-owned signals

`Ruya.OpenTelemetry` does not subscribe to optional sibling libraries. The deployable application owns the
exact set of library and application signals it uses and lists their documented names through
`OpenTelemetry:ActivitySources` and `OpenTelemetry:Meters`. Source and meter names must be nonblank.

## Optional HTTP body capture

Body capture is disabled by default. When a concrete diagnostic requirement justifies it, enable the request and/or response setting and add the middleware after routing:

```csharp
app.UseRouting();
app.UseHttpBodyCapture();
```

The middleware captures at most `MaxBodySizeBytes`, streams responses directly to the caller, and emits only valid JSON after configured JSON-path redaction. Oversized, invalid JSON, and non-JSON bodies are represented by bounded marker values rather than raw content. Outbound `HttpClient` bodies are not captured because OpenTelemetry enrichment callbacks are synchronous and must not block asynchronous content reads.

Trace and log access still require normal production access controls. Do not enable body capture for payloads whose sensitive fields cannot be completely and reliably redacted.
