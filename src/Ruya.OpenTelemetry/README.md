
### 1. Register Services

In `Program.cs`:

```csharp
builder.ConfigureOpenTelemetry();
```

### 2. Configure Settings

In `appsettings.json`:

```json
{
  "OpenTelemetry": {
    "Service": {
      "Name": "MyService",
      "Version": "1.0.0",
      "Namespace": "MyNamespace"
    },
    "Http": {
      "RecordException": true,
      "CaptureBody": true
    },
    "Sql": {
      "CaptureParameters": false
    }
  },
  "OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4317"
}
```

### 3. Add Middleware (Optional)

To capture HTTP bodies (if enabled in settings):

```csharp
app.UseRouting();
app.UseHttpBodyCapture(); // Must be after UseRouting
app.MapControllers();
```

## Usage

The library automatically collects telemetry. You can also use `ActivitySource` for manual tracing:

```csharp
public class MyService
{
    private static readonly ActivitySource ActivitySource = new("MyService");

    public void DoWork()
    {
        using var activity = ActivitySource.StartActivity("DoingWork");
        // ...
    }
}
```
