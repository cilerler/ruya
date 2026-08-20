using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.AspNetCore.Diagnostics.GlobalExceptionHandler;

namespace Ruya.AspNetCore.Diagnostics.Unit.Tests;

[TestClass]
public class GlobalExceptionHandlerServiceTests
{
	private Mock<ILogger<GlobalExceptionHandlerService>> _loggerMock = null!;
	private GlobalExceptionHandlerService _sut = null!;

	[TestInitialize]
	public void Setup()
	{
		_loggerMock = new Mock<ILogger<GlobalExceptionHandlerService>>();
		_loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
		_sut = new GlobalExceptionHandlerService(_loggerMock.Object);
	}

	private static DefaultHttpContext CreateHttpContext()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ILoggerFactory, LoggerFactory>();
		services.ConfigureHttpJsonOptions(options => { });
		var serviceProvider = services.BuildServiceProvider();

		var context = new DefaultHttpContext
		{
			RequestServices = serviceProvider,
			Response = { Body = new MemoryStream() }
		};
		return context;
	}

	private void VerifyLoggedEvent(LogLevel logLevel, int eventId, Exception? exception)
	{
#pragma warning disable CA1873 // Moq expression matchers are not evaluated as production log arguments.
		_loggerMock.Verify(
			x => x.Log(
				logLevel,
				It.Is<EventId>(candidate => candidate.Id == eventId),
				It.IsAny<It.IsAnyType>(),
				exception,
				It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
			Times.Once);
#pragma warning restore CA1873
	}

	#region Input Validation Tests

	[TestMethod]
	public void Constructor_NullLogger_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new GlobalExceptionHandlerService(null!));
	}

	[TestMethod]
	public async Task TryHandleAsync_NullHttpContext_ThrowsArgumentNullException()
	{
		// Arrange
		var exception = new InvalidOperationException("Test");

		// Act & Assert
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => _sut.TryHandleAsync(null!, exception, CancellationToken.None).AsTask());
	}

	[TestMethod]
	public async Task TryHandleAsync_NullException_ThrowsArgumentNullException()
	{
		// Arrange
		var httpContext = CreateHttpContext();

		// Act & Assert
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => _sut.TryHandleAsync(httpContext, null!, CancellationToken.None).AsTask());
	}

	#endregion

	#region Exception Mapping Tests

	[TestMethod]
	public async Task TryHandleAsync_ArgumentNullException_Returns400()
	{
		// Arrange
		var httpContext = CreateHttpContext();
#pragma warning disable S3928 // The synthetic exception validates mapping and has no originating method parameter.
		var exception = new ArgumentNullException("request", "Required request value was null.");
#pragma warning restore S3928

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_ArgumentOutOfRangeException_Returns400()
	{
		// Arrange
		var httpContext = CreateHttpContext();
#pragma warning disable S3928 // The synthetic exception validates mapping and has no originating method parameter.
		var exception = new ArgumentOutOfRangeException("value", "Request value was outside the supported range.");
#pragma warning restore S3928

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_ArgumentException_Returns400()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new ArgumentException("Invalid argument");

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_UnauthorizedAccessException_Returns403()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new UnauthorizedAccessException("Access denied");

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_NotImplementedException_Returns501()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new NotImplementedException("Not implemented");

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status501NotImplemented, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_TimeoutException_Returns504()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new TimeoutException("Timeout");

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status504GatewayTimeout, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_OperationCanceledException_WithoutRequestCancellation_Returns500()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new OperationCanceledException("Cancelled");

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_GenericException_Returns500()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new InvalidOperationException("Something went wrong");

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
	}

	#endregion

	#region Cancellation Handling Tests

	[TestMethod]
	public async Task TryHandleAsync_OperationCanceledException_WithRequestCancellation_Returns499()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		var exception = new OperationCanceledException(cts.Token);

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, cts.Token);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(499, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_TaskCanceledException_WithRequestCancellation_Returns499()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		var exception = new TaskCanceledException("Task was cancelled", null, cts.Token);

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, cts.Token);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(499, httpContext.Response.StatusCode);
	}

	[TestMethod]
	public async Task TryHandleAsync_OperationCanceledException_WithOnlyExceptionTokenCancelled_Returns500()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		var exception = new OperationCanceledException(cts.Token);

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
		Assert.AreEqual(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
	}

	#endregion

	#region Logging Tests

	[TestMethod]
	public async Task TryHandleAsync_Exception_LogsError()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new InvalidOperationException("Test error");

		// Act
		await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		VerifyLoggedEvent(LogLevel.Error, eventId: 1001, exception);
	}

	[TestMethod]
	public async Task TryHandleAsync_CancelledRequest_LogsInformation()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		var exception = new OperationCanceledException(cts.Token);

		// Act
		await _sut.TryHandleAsync(httpContext, exception, cts.Token);

		// Assert
		VerifyLoggedEvent(LogLevel.Information, eventId: 1002, exception: null);
	}

	#endregion

	#region Activity/OpenTelemetry Tests

	[TestMethod]
	public async Task TryHandleAsync_WithActivity_SetsErrorStatus()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new InvalidOperationException("Test error");

		using var activitySource = new ActivitySource("TestSource");
		using var listener = new ActivityListener
		{
			ShouldListenTo = _ => true,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
		};
		ActivitySource.AddActivityListener(listener);

		using var activity = activitySource.StartActivity("TestActivity");

		// Act
		await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsNotNull(activity);
		Assert.AreEqual(ActivityStatusCode.Error, activity.Status);
	}

	[TestMethod]
	public async Task TryHandleAsync_WithActivity_AddsExceptionEvent()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new InvalidOperationException("Test error");

		using var activitySource = new ActivitySource("TestSource");
		using var listener = new ActivityListener
		{
			ShouldListenTo = _ => true,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
		};
		ActivitySource.AddActivityListener(listener);

		using var activity = activitySource.StartActivity("TestActivity");

		// Act
		await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsNotNull(activity);
		var events = activity.Events.ToList();
		Assert.AreEqual(1, events.Count);
		Assert.AreEqual("exception", events[0].Name);

		var tags = events[0].Tags.ToDictionary(t => t.Key, t => t.Value);
		Assert.AreEqual(typeof(InvalidOperationException).FullName, tags["exception.type"]);
		Assert.AreEqual("Test error", tags["exception.message"]);
		Assert.AreEqual(500, tags["exception.statuscode"]);
	}

	#endregion

	#region TraceId Tests

	[TestMethod]
	public async Task TryHandleAsync_WithActivity_UsesActivityId()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		httpContext.TraceIdentifier = "http-trace-id";
		var exception = new InvalidOperationException("Test error");

		using var activitySource = new ActivitySource("TestSource");
		using var listener = new ActivityListener
		{
			ShouldListenTo = _ => true,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
		};
		ActivitySource.AddActivityListener(listener);

		using var activity = activitySource.StartActivity("TestActivity");

		// Act
		await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert - Response body should contain the activity ID, not the HTTP trace identifier
		httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
		using var reader = new StreamReader(httpContext.Response.Body);
		var responseBody = await reader.ReadToEndAsync();

		Assert.IsNotNull(activity?.Id);
		Assert.IsTrue(responseBody.Contains(activity.Id, StringComparison.Ordinal), "Response should contain Activity.Current.Id");
	}

	[TestMethod]
	public async Task TryHandleAsync_WithoutActivity_UsesHttpTraceIdentifier()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		httpContext.TraceIdentifier = "http-trace-id-12345";
		var exception = new InvalidOperationException("Test error");

		// Act
		await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
		using var reader = new StreamReader(httpContext.Response.Body);
		var responseBody = await reader.ReadToEndAsync();

		Assert.IsTrue(responseBody.Contains("http-trace-id-12345", StringComparison.Ordinal), "Response should contain HttpContext.TraceIdentifier");
	}

	#endregion

	#region Response Content Tests

	[TestMethod]
	public async Task TryHandleAsync_ReturnsTrue_WhenExceptionHandled()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new InvalidOperationException("Test");

		// Act
		var result = await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(result);
	}

	[TestMethod]
	public async Task TryHandleAsync_SetsProblemDetailsContentType()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var exception = new InvalidOperationException("Test");

		// Act
		await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		Assert.IsTrue(httpContext.Response.ContentType?.Contains("application/problem+json", StringComparison.Ordinal) ?? false);
	}

	[TestMethod]
	public async Task TryHandleAsync_DoesNotExposeExceptionDetails()
	{
		// Arrange
		var httpContext = CreateHttpContext();
		var sensitiveMessage = "Database connection string: Server=secret;Password=hunter2";
		var exception = new InvalidOperationException(sensitiveMessage);

		// Act
		await _sut.TryHandleAsync(httpContext, exception, CancellationToken.None);

		// Assert
		httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
		using var reader = new StreamReader(httpContext.Response.Body);
		var responseBody = await reader.ReadToEndAsync();

		Assert.IsFalse(responseBody.Contains(sensitiveMessage, StringComparison.Ordinal), "Response should not contain sensitive exception message");
		Assert.IsFalse(responseBody.Contains("hunter2", StringComparison.Ordinal), "Response should not contain sensitive data");
		Assert.IsTrue(responseBody.Contains("An unexpected error occurred", StringComparison.Ordinal), "Response should contain generic message");
	}

	#endregion
}
