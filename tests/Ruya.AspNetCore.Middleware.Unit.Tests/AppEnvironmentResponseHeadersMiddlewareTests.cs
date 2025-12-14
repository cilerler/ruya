using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.AspNetCore.Middleware.AppEnvironmentResponseHeaders;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Ruya.AspNetCore.Middleware.Unit.Tests;

[TestClass]
public class AppEnvironmentResponseHeadersMiddlewareTests
{
	private static DefaultHttpContext CreateHttpContext()
	{
		var context = new DefaultHttpContext
		{
			Response = { Body = new MemoryStream() }
		};
		return context;
	}

	private static Mock<ILogger<AppEnvironmentResponseHeadersMiddleware>> CreateLoggerMock()
	{
		var loggerMock = new Mock<ILogger<AppEnvironmentResponseHeadersMiddleware>>();
		loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
		return loggerMock;
	}

	private static AppEnvironmentResponseHeadersMiddleware CreateMiddleware(
		RequestDelegate next,
		AppEnvironmentResponseHeadersSettings? settings = null,
		Mock<ILogger<AppEnvironmentResponseHeadersMiddleware>>? loggerMock = null)
	{
		settings ??= new AppEnvironmentResponseHeadersSettings { Enabled = true };
		loggerMock ??= CreateLoggerMock();
		var options = Options.Create(settings);

		return new AppEnvironmentResponseHeadersMiddleware(
			next,
			loggerMock.Object,
			options);
	}

	#region Constructor Validation Tests

	[TestMethod]
	public void Constructor_NullNext_ThrowsArgumentNullException()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings();
		var options = Options.Create(settings);
		var loggerMock = CreateLoggerMock();

		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new AppEnvironmentResponseHeadersMiddleware(
				null!,
				loggerMock.Object,
				options));
	}

	[TestMethod]
	public void Constructor_NullLogger_ThrowsArgumentNullException()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings();
		var options = Options.Create(settings);
		RequestDelegate next = _ => Task.CompletedTask;

		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new AppEnvironmentResponseHeadersMiddleware(
				next,
				null!,
				options));
	}

	[TestMethod]
	public void Constructor_NullOptions_ThrowsArgumentNullException()
	{
		// Arrange
		var loggerMock = CreateLoggerMock();
		RequestDelegate next = _ => Task.CompletedTask;

		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new AppEnvironmentResponseHeadersMiddleware(
				next,
				loggerMock.Object,
				null!));
	}

	#endregion

	#region InvokeAsync Validation Tests

	[TestMethod]
	public async Task InvokeAsync_NullHttpContext_ThrowsArgumentNullException()
	{
		// Arrange
		var middleware = CreateMiddleware(_ => Task.CompletedTask);

		// Act & Assert
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => middleware.InvokeAsync(null!));
	}

	#endregion

	#region Middleware Enabled/Disabled Tests

	[TestMethod]
	public async Task InvokeAsync_WhenDisabled_DoesNotAddHeaders()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings { Enabled = false };
		var nextCalled = false;
		RequestDelegate next = _ =>
		{
			nextCalled = true;
			return Task.CompletedTask;
		};

		var middleware = CreateMiddleware(next, settings);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);

		// Assert
		Assert.IsTrue(nextCalled, "Next middleware should be called");
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-ApplicationVersion"));
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-ApplicationName"));
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-Environment"));
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-MachineName"));
	}

	[TestMethod]
	public async Task InvokeAsync_WhenDisabled_LogsMiddlewareDisabled()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings { Enabled = false };
		var loggerMock = CreateLoggerMock();
		var middleware = CreateMiddleware(_ => Task.CompletedTask, settings, loggerMock);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);

		// Assert
		loggerMock.Verify(
			x => x.Log(
				LogLevel.Debug,
				It.IsAny<EventId>(),
				It.IsAny<It.IsAnyType>(),
				It.IsAny<Exception?>(),
				It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
			Times.Once);
	}

	[TestMethod]
	public async Task InvokeAsync_WhenEnabled_AddsDefaultHeaders()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings
		{
			Enabled = true,
			IncludeVersion = true,
			IncludeName = true,
			IncludeEnvironment = true,
			IncludeMachineName = false
		};

		var middleware = CreateMiddleware(_ => Task.CompletedTask, settings);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);
		
		// Assert
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-ApplicationVersion"));
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-ApplicationName"));
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-Environment"));
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-MachineName"));
	}

	#endregion

	#region Header Configuration Tests

	[TestMethod]
	public async Task InvokeAsync_IncludeVersionFalse_DoesNotAddVersionHeader()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings
		{
			Enabled = true,
			IncludeVersion = false,
			IncludeName = true,
			IncludeEnvironment = true
		};

		var middleware = CreateMiddleware(_ => Task.CompletedTask, settings);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);
		
		// Assert
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-ApplicationVersion"));
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-ApplicationName"));
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-Environment"));
	}

	[TestMethod]
	public async Task InvokeAsync_IncludeNameFalse_DoesNotAddNameHeader()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings
		{
			Enabled = true,
			IncludeVersion = true,
			IncludeName = false,
			IncludeEnvironment = true
		};

		var middleware = CreateMiddleware(_ => Task.CompletedTask, settings);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);
		
		// Assert
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-ApplicationVersion"));
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-ApplicationName"));
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-Environment"));
	}

	[TestMethod]
	public async Task InvokeAsync_IncludeEnvironmentFalse_DoesNotAddEnvironmentHeader()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings
		{
			Enabled = true,
			IncludeVersion = true,
			IncludeName = true,
			IncludeEnvironment = false
		};

		var middleware = CreateMiddleware(_ => Task.CompletedTask, settings);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);
		
		// Assert
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-ApplicationVersion"));
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-ApplicationName"));
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-Environment"));
	}

	[TestMethod]
	public async Task InvokeAsync_IncludeMachineNameTrue_AddsMachineNameHeader()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings
		{
			Enabled = true,
			IncludeMachineName = true
		};

		var middleware = CreateMiddleware(_ => Task.CompletedTask, settings);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);
		
		// Assert
		Assert.IsTrue(context.Response.Headers.ContainsKey("X-MachineName"));
		Assert.AreEqual(Environment.MachineName, context.Response.Headers["X-MachineName"].ToString());
	}

	[TestMethod]
	public async Task InvokeAsync_DefaultSettings_DoesNotIncludeMachineName()
	{
		// Arrange - Default settings should have IncludeMachineName = false for security
		var settings = new AppEnvironmentResponseHeadersSettings { Enabled = true };

		var middleware = CreateMiddleware(_ => Task.CompletedTask, settings);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);
		
		// Assert
		Assert.IsFalse(context.Response.Headers.ContainsKey("X-MachineName"),
			"Machine name should not be included by default for security reasons");
	}

	#endregion

	#region Next Middleware Tests

	[TestMethod]
	public async Task InvokeAsync_CallsNextMiddleware()
	{
		// Arrange
		var nextCalled = false;
		RequestDelegate next = _ =>
		{
			nextCalled = true;
			return Task.CompletedTask;
		};

		var middleware = CreateMiddleware(next);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);

		// Assert
		Assert.IsTrue(nextCalled, "Next middleware should be called");
	}

	[TestMethod]
	public async Task InvokeAsync_NextMiddlewareThrows_PropagatesException()
	{
		// Arrange
		var expectedException = new InvalidOperationException("Test exception");
		RequestDelegate next = _ => throw expectedException;

		var middleware = CreateMiddleware(next);
		var context = CreateHttpContext();

		// Act & Assert
		var actualException = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => middleware.InvokeAsync(context));

		Assert.AreSame(expectedException, actualException);
	}

	#endregion

	#region Logging Tests

	[TestMethod]
	public async Task InvokeAsync_WhenEnabled_LogsHeadersAdded()
	{
		// Arrange
		var settings = new AppEnvironmentResponseHeadersSettings { Enabled = true };
		var loggerMock = CreateLoggerMock();
		var middleware = CreateMiddleware(_ => Task.CompletedTask, settings, loggerMock);
		var context = CreateHttpContext();

		// Act
		await middleware.InvokeAsync(context);
		
		// Assert
		loggerMock.Verify(
			x => x.Log(
				LogLevel.Debug,
				It.IsAny<EventId>(),
				It.IsAny<It.IsAnyType>(),
				It.IsAny<Exception?>(),
				It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
			Times.Once);
	}

	#endregion
}

[TestClass]
public class AppEnvironmentResponseHeadersSettingsTests
{
	[TestMethod]
	public void DefaultSettings_IncludeMachineName_IsFalse()
	{
		// Arrange & Act
		var settings = new AppEnvironmentResponseHeadersSettings();

		// Assert
		Assert.IsFalse(settings.IncludeMachineName,
			"IncludeMachineName should be false by default for security");
	}

	[TestMethod]
	public void DefaultSettings_IncludeVersion_IsTrue()
	{
		// Arrange & Act
		var settings = new AppEnvironmentResponseHeadersSettings();

		// Assert
		Assert.IsTrue(settings.IncludeVersion);
	}

	[TestMethod]
	public void DefaultSettings_IncludeName_IsTrue()
	{
		// Arrange & Act
		var settings = new AppEnvironmentResponseHeadersSettings();

		// Assert
		Assert.IsTrue(settings.IncludeName);
	}

	[TestMethod]
	public void DefaultSettings_IncludeEnvironment_IsTrue()
	{
		// Arrange & Act
		var settings = new AppEnvironmentResponseHeadersSettings();

		// Assert
		Assert.IsTrue(settings.IncludeEnvironment);
	}

	[TestMethod]
	public void DefaultSettings_Enabled_IsFalse()
	{
		// Arrange & Act
		var settings = new AppEnvironmentResponseHeadersSettings();

		// Assert
		Assert.IsFalse(settings.Enabled,
			"Enabled should be false by default - requires explicit opt-in");
	}

	[TestMethod]
	public void ConfigurationSectionName_IsNotNullOrEmpty()
	{
		// Assert
		Assert.IsFalse(string.IsNullOrEmpty(AppEnvironmentResponseHeadersSettings.ConfigurationSectionName),
			"ConfigurationSectionName should not be null or empty");
	}

	[TestMethod]
	public void FeatureFlag_MatchesConfigurationSectionName()
	{
		// Assert
		Assert.AreEqual(AppEnvironmentResponseHeadersSettings.ConfigurationSectionName,
			AppEnvironmentResponseHeadersSettings.FeatureFlag);
	}
}

[TestClass]
public class StartupExtensionsTests
{
	[TestMethod]
	public void AddAppEnvironmentResponseHeaders_NullServices_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			StartupExtensions.AddAppEnvironmentResponseHeaders(null!));
	}

	[TestMethod]
	public void UseAppEnvironmentResponseHeaders_NullBuilder_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			StartupExtensions.UseAppEnvironmentResponseHeaders(null!));
	}
}
