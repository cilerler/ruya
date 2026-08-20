using System;

using Microsoft.Extensions.Logging;

namespace Ruya.OpenTelemetry;

internal static partial class Log
{
    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Debug,
        Message = "Failed to capture HTTP {BodyKind} body")]
    public static partial void BodyCaptureFailed(this ILogger logger, string bodyKind, Exception exception);
}
