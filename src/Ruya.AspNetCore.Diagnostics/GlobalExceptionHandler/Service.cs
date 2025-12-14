using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ruya.AspNetCore.Diagnostics.GlobalExceptionHandler;

public sealed class GlobalExceptionHandlerService(ILogger<GlobalExceptionHandlerService> logger) : IExceptionHandler
{
	private const string TraceIdKey = "traceId";
	private const string ExceptionTypeKey = "exception.type";
	private const string ExceptionMessageKey = "exception.message";
	private const string ExceptionStatusCodeKey = "exception.statuscode";

	private readonly ILogger _logger = logger;

	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(httpContext);
		ArgumentNullException.ThrowIfNull(exception);

		string traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

		// Handle client cancellation separately
		if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
		{
			_logger.RequestCancelled(traceId);
			// Let the framework handle cancelled requests
			return false;
		}

		(int statusCode, string title) = MapException(exception);

		RecordExceptionOnActivity(exception, statusCode);

		_logger.UnhandledException(exception, traceId, exception.GetType().Name, statusCode);

		await Results.Problem(
			title: title,
			statusCode: statusCode,
			extensions: new Dictionary<string, object?>(capacity: 1)
			{
				{ TraceIdKey, traceId }
			}).ExecuteAsync(httpContext);

		return true;
	}

	private static void RecordExceptionOnActivity(Exception exception, int statusCode)
	{
		var activity = Activity.Current;
		if (activity is null)
		{
			return;
		}

		activity.SetStatus(ActivityStatusCode.Error, exception.Message);
		activity.AddEvent(new ActivityEvent("exception",
			tags:
			[
				new(ExceptionTypeKey, exception.GetType().FullName),
				new(ExceptionMessageKey, exception.Message),
				new(ExceptionStatusCodeKey, statusCode)
			]));
	}

	private static (int StatusCode, string Title) MapException(Exception exception)
	{
		return exception switch
		{
			ArgumentNullException or ArgumentOutOfRangeException or ArgumentException
				=> (StatusCodes.Status400BadRequest, "Invalid request parameters"),
			UnauthorizedAccessException
				=> (StatusCodes.Status403Forbidden, "Access denied"),
			NotImplementedException
				=> (StatusCodes.Status501NotImplemented, "Feature not implemented"),
			TimeoutException
				=> (StatusCodes.Status504GatewayTimeout, "Operation timed out"),
			OperationCanceledException
				=> (499, "Client closed request"),
			_ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
		};
	}
}
