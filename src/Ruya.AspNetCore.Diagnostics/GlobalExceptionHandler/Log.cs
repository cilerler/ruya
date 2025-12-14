using System;
using Microsoft.Extensions.Logging;

namespace Ruya.AspNetCore.Diagnostics.GlobalExceptionHandler;

/// <summary>
/// High-performance logging extensions using source generators.
/// </summary>
internal static partial class Log
{
	[LoggerMessage(
		EventId = LogEvents.UnhandledException,
		Level = LogLevel.Error,
		Message = "Unhandled exception. [TraceId = {TraceId}, ExceptionType = {ExceptionType}, StatusCode = {StatusCode}]")]
	public static partial void UnhandledException(this ILogger logger, Exception exception, string traceId, string exceptionType, int statusCode);

	[LoggerMessage(
		EventId = LogEvents.RequestCancelled,
		Level = LogLevel.Information,
		Message = "Request cancelled by client. [TraceId = {TraceId}]")]
	public static partial void RequestCancelled(this ILogger logger, string traceId);
}

/// <summary>
/// Centralized log event IDs for the GlobalExceptionHandler.
/// </summary>
internal static class LogEvents
{
	/// <summary>Unhandled exception occurred.</summary>
	public const int UnhandledException = 1001;

	/// <summary>Request was cancelled by the client.</summary>
	public const int RequestCancelled = 1002;
}
