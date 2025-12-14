using Microsoft.Extensions.Logging;

namespace Ruya.AspNetCore.Middleware.AppEnvironmentResponseHeaders;

/// <summary>
/// High-performance logging extensions using source generators.
/// </summary>
internal static partial class Log
{
	[LoggerMessage(
		EventId = LogEvents.HeadersAdded,
		Level = LogLevel.Debug,
		Message = "Response headers added. [HeaderCount = {HeaderCount}]")]
	public static partial void HeadersAdded(this ILogger logger, int headerCount);

	[LoggerMessage(
		EventId = LogEvents.MiddlewareDisabled,
		Level = LogLevel.Debug,
		Message = "Middleware is disabled, skipping header injection.")]
	public static partial void MiddlewareDisabled(this ILogger logger);
}

/// <summary>
/// Centralized log event IDs for the AppEnvironmentResponseHeaders middleware.
/// </summary>
internal static class LogEvents
{
	/// <summary>Response headers were added successfully.</summary>
	public const int HeadersAdded = 1001;

	/// <summary>Middleware is disabled via configuration.</summary>
	public const int MiddlewareDisabled = 1002;
}
