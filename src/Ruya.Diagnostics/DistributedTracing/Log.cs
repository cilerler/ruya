using System;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Ruya.Diagnostics.DistributedTracing;

internal static partial class Log
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Debug, Message = "Trace-context cache miss; using fallback parent for activity {ActivityName}")]
    public static partial void CacheMissUsingFallback(this ILogger logger, string activityName);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Trace-context cache miss without fallback; activity {ActivityName} starts a new trace")]
    public static partial void CacheMissWithoutFallback(this ILogger logger, string activityName);

    [LoggerMessage(EventId = 102, Level = LogLevel.Debug, Message = "Started activity {ActivityName} TraceId={TraceId} SpanId={SpanId} ParentSpanId={ParentSpanId}")]
    public static partial void ActivityStarted(this ILogger logger, string activityName, ActivityTraceId traceId, ActivitySpanId spanId, ActivitySpanId parentSpanId);

    [LoggerMessage(EventId = 103, Level = LogLevel.Debug, Message = "Stopped activity {ActivityName} TraceId={TraceId} SpanId={SpanId} Status={Status}")]
    public static partial void ActivityStopped(this ILogger logger, string activityName, ActivityTraceId traceId, ActivitySpanId spanId, ActivityStatusCode status);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning, Message = "Failed to retrieve cached trace context")]
    public static partial void CacheReadFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 105, Level = LogLevel.Warning, Message = "Failed to cache trace context")]
    public static partial void CacheWriteFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 106, Level = LogLevel.Debug, Message = "Cached trace context")]
    public static partial void TraceContextCached(this ILogger logger);

    [LoggerMessage(EventId = 107, Level = LogLevel.Warning, Message = "Disposing distributed tracing with {Count} active activities")]
    public static partial void ActiveActivitiesOnDispose(this ILogger logger, int count);
}
