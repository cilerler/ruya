using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Ruya.AspNetCore.DataProtection.StackExchangeRedis.Contracts;

using StackExchange.Redis;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis.Unit.Tests;

[TestClass]
public class DataProtectionHealthCheckTests
{
	private static Mock<ILogger<DataProtectionHealthCheck>> CreateLoggerMock()
	{
		var loggerMock = new Mock<ILogger<DataProtectionHealthCheck>>();
		loggerMock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
		return loggerMock;
	}

	private static Mock<IConnectionMultiplexer> CreateConnectionMultiplexerMock(
		bool isConnected = true,
		TimeSpan? pingLatency = null)
	{
		var mock = new Mock<IConnectionMultiplexer>();
		mock.Setup(x => x.IsConnected).Returns(isConnected);

		var databaseMock = new Mock<IDatabase>();
		databaseMock.Setup(x => x.PingAsync(It.IsAny<CommandFlags>()))
			.ReturnsAsync(pingLatency ?? TimeSpan.FromMilliseconds(1));

		mock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
			.Returns(databaseMock.Object);

		return mock;
	}

	private static Mock<IDataProtection> CreateDataProtectionMock(
		bool roundtripSucceeds = true)
	{
		var mock = new Mock<IDataProtection>();
		mock.Setup(x => x.Protect(It.IsAny<string>(), It.IsAny<IEnumerable<string>?>()))
			.Returns((string input, IEnumerable<string>? _) => $"protected:{input}");

		if (roundtripSucceeds)
		{
			mock.Setup(x => x.Unprotect(It.IsAny<string>(), It.IsAny<IEnumerable<string>?>()))
				.Returns((string input, IEnumerable<string>? _) => input.Replace("protected:", "", StringComparison.Ordinal));
		}
		else
		{
			mock.Setup(x => x.Unprotect(It.IsAny<string>(), It.IsAny<IEnumerable<string>?>()))
				.Returns("wrong-content");
		}

		return mock;
	}

	#region Constructor Tests

	[TestMethod]
	public void Constructor_NullLogger_ThrowsArgumentNullException()
	{
		// Arrange
		var serviceProvider = new Mock<IServiceProvider>().Object;

		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new DataProtectionHealthCheck(null!, serviceProvider));
	}

	[TestMethod]
	public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
	{
		// Arrange
		var logger = CreateLoggerMock().Object;

		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new DataProtectionHealthCheck(logger, null!));
	}

	#endregion

	#region Server Mode (No AsyncLazy) Tests

	[TestMethod]
	public async Task CheckHealthAsync_ServerMode_Healthy_ReturnsHealthy()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddSingleton(CreateConnectionMultiplexerMock().Object);
		services.AddSingleton(CreateDataProtectionMock().Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Healthy, result.Status);
		Assert.IsTrue(result.Description?.Contains("Redis ping:") ?? false);
		Assert.IsTrue(result.Description?.Contains("Data protection: OK") ?? false);
	}

	[TestMethod]
	public async Task CheckHealthAsync_ServerMode_RedisNotConnected_ReturnsUnhealthy()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddSingleton(CreateConnectionMultiplexerMock(isConnected: false).Object);
		services.AddSingleton(CreateDataProtectionMock().Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
		Assert.AreEqual("Redis connection is not established.", result.Description);
	}

	[TestMethod]
	public async Task CheckHealthAsync_ServerMode_HighLatency_ReturnsDegraded()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddSingleton(CreateConnectionMultiplexerMock(pingLatency: TimeSpan.FromSeconds(6)).Object);
		services.AddSingleton(CreateDataProtectionMock().Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Degraded, result.Status);
		Assert.IsTrue(result.Description?.Contains("Redis ping latency is high") ?? false);
	}

	[TestMethod]
	public async Task CheckHealthAsync_ServerMode_RoundtripFails_ReturnsUnhealthy()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddSingleton(CreateConnectionMultiplexerMock().Object);
		services.AddSingleton(CreateDataProtectionMock(roundtripSucceeds: false).Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
		Assert.AreEqual("Data protection roundtrip failed: content mismatch.", result.Description);
	}

	#endregion

	#region Client Mode (With AsyncLazy) Tests

	[TestMethod]
	public async Task CheckHealthAsync_ClientMode_SettingsNotInitialized_ReturnsUnhealthy()
	{
		// Arrange
		var lazySettings = new AsyncLazy<DataProtectionSettings>(() =>
		{
			var tcs = new TaskCompletionSource<DataProtectionSettings>();
			// Never complete - simulates not yet initialized
			return tcs.Task;
		});

		var services = new ServiceCollection();
		services.AddSingleton(lazySettings);
		services.AddSingleton(CreateConnectionMultiplexerMock().Object);
		services.AddSingleton(CreateDataProtectionMock().Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
		Assert.AreEqual("Data protection settings are not yet initialized.", result.Description);
	}

	[TestMethod]
	public async Task CheckHealthAsync_ClientMode_RedisNotInitialized_ReturnsUnhealthy()
	{
		// Arrange
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys"
		};
		var lazySettings = new AsyncLazy<DataProtectionSettings>(() => Task.FromResult(settings));
		// Force initialization
		_ = await lazySettings.Value;

		var lazyRedis = new AsyncLazy<IConnectionMultiplexer>(() =>
		{
			var tcs = new TaskCompletionSource<IConnectionMultiplexer>();
			return tcs.Task;
		});

		var services = new ServiceCollection();
		services.AddSingleton(lazySettings);
		services.AddSingleton(lazyRedis);
		services.AddSingleton(CreateConnectionMultiplexerMock().Object);
		services.AddSingleton(CreateDataProtectionMock().Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
		Assert.AreEqual("Redis connection is not yet initialized.", result.Description);
	}

	[TestMethod]
	public async Task CheckHealthAsync_ClientMode_FullyInitialized_ReturnsHealthy()
	{
		// Arrange
		var settings = new DataProtectionSettings
		{
			ApplicationName = "Test",
			ConnectionStringKey = "Redis",
			CacheKey = "Keys"
		};
		var lazySettings = new AsyncLazy<DataProtectionSettings>(() => Task.FromResult(settings));
		_ = await lazySettings.Value;

		var connectionMock = CreateConnectionMultiplexerMock();
		var lazyRedis = new AsyncLazy<IConnectionMultiplexer>(() => Task.FromResult(connectionMock.Object));
		_ = await lazyRedis.Value;

		var services = new ServiceCollection();
		services.AddSingleton(lazySettings);
		services.AddSingleton(lazyRedis);
		services.AddSingleton(connectionMock.Object);
		services.AddSingleton(CreateDataProtectionMock().Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Healthy, result.Status);
	}

	#endregion

	#region Exception Handling Tests

	[TestMethod]
	public async Task CheckHealthAsync_ExceptionThrown_ReturnsUnhealthy()
	{
		// Arrange
		var connectionMock = new Mock<IConnectionMultiplexer>();
		connectionMock.Setup(x => x.IsConnected).Throws(new InvalidOperationException("Test exception"));

		var services = new ServiceCollection();
		services.AddSingleton(connectionMock.Object);
		services.AddSingleton(CreateDataProtectionMock().Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
		Assert.AreEqual("Data protection health check failed.", result.Description);
		Assert.IsNotNull(result.Exception);
	}

	[TestMethod]
	public async Task CheckHealthAsync_DataProtectionThrows_ReturnsUnhealthy()
	{
		// Arrange
		var dataProtectionMock = new Mock<IDataProtection>();
		dataProtectionMock.Setup(x => x.Protect(It.IsAny<string>()))
			.Throws(new InvalidOperationException("Encryption failed"));

		var services = new ServiceCollection();
		services.AddSingleton(CreateConnectionMultiplexerMock().Object);
		services.AddSingleton(dataProtectionMock.Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		// Assert
		Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
		Assert.IsNotNull(result.Exception);
	}

	#endregion

	#region Cancellation Tests

	[TestMethod]
	public async Task CheckHealthAsync_CancellationRequested_StillCompletes()
	{
		// Arrange - health check doesn't currently use cancellation token
		// but should handle it gracefully
		var services = new ServiceCollection();
		services.AddSingleton(CreateConnectionMultiplexerMock().Object);
		services.AddSingleton(CreateDataProtectionMock().Object);

		var serviceProvider = services.BuildServiceProvider();
		var logger = CreateLoggerMock();
		var healthCheck = new DataProtectionHealthCheck(logger.Object, serviceProvider);

		using var cts = new CancellationTokenSource();

		// Act
		var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), cts.Token);

		// Assert
		Assert.AreEqual(HealthStatus.Healthy, result.Status);
	}

	#endregion
}
