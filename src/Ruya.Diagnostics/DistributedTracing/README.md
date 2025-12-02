# Distributed Tracing

## Troubleshooting

```text
Value cannot be null. (Parameter 'Activity is null')
System.ArgumentNullException: Value cannot be null. (Parameter 'Activity is null')
   at Ruya.Diagnostics.DistributedTracing.DistributedTracingService.StartOrContinueActivity(String activityName, ActivityKind activityKind, String parentKey, Boolean skipParentCheck)
   at Ruya.Diagnostics.DistributedTracing.DistributedTracingService.StartActivity(String activityName, ActivityKind activityKind, String parentKey)
```

This error suggests that `ActivityListener` might not be configured correctly.  
To diagnose the issue, you can use the provided configuration snippet below to test if it resolves the error.  
However, this is a diagnose only workaroud, as **OpenTelemetry** should ideally handle this configuration.

Add the following code snippet right after you call `builder.Services.AddDistributedTracingService();`.

```csharp
ActivityListener listener = new ActivityListener
{
    ShouldListenTo = (source) => source.Name == Ruya.Primitives.Startup.AssemblyName,
    Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
};
ActivitySource.AddActivityListener(listener);
```
