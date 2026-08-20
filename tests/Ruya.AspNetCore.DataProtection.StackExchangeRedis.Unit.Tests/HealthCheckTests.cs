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
		StringAssert.Contains(result.Description, "Redis ping:", StringComparison.Ordinal);
		StringAssert.Contains(result.Description, "Data protection: OK", StringComparison.Ordinal);
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
		StringAssert.Contains(result.Description, "Redis ping latency is high", StringComparison.Ordinal);
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

	[TestMethod]
	public async Task CheckHealthAsync_RemoteInitializationFailsThenRecovers_ReturnsHealthyOnRetry()
	{
		var settingsAttempts = 0;
		var connection = CreateConnectionMultiplexerMock();
		var remoteSettings = new AsyncLazy<DataProtectionSettings>(() =>
		{
			var attempt = Interlocked.Increment(ref settingsAttempts);
			return attempt == 1
				? Task.FromException<DataProtectionSettings>(new InvalidOperationException("transient"))
				: Task.FromResult(new DataProtectionSettings
				{
					ApplicationName = "remote-client",
					ConnectionStringKey = "Redis",
					ConnectionString = "localhost:6379",
					CacheKey = "keys"
				});
		});
		var remoteConnection = new AsyncLazy<IConnectionMultiplexer>(
			() => Task.FromResult(connection.Object));
		var services = new ServiceCollection();
		services.AddSingleton(remoteSettings);
		services.AddSingleton(remoteConnection);
		services.AddSingleton(connection.Object);
		services.AddSingleton(CreateDataProtectionMock().Object);
		using var serviceProvider = services.BuildServiceProvider();
		var healthCheck = new DataProtectionHealthCheck(CreateLoggerMock().Object, serviceProvider);

		var failedResult = await healthCheck.CheckHealthAsync(new HealthCheckContext());
		var recoveredResult = await healthCheck.CheckHealthAsync(new HealthCheckContext());

		Assert.AreEqual(HealthStatus.Unhealthy, failedResult.Status);
		Assert.AreEqual(HealthStatus.Healthy, recoveredResult.Status);
		Assert.AreEqual(2, settingsAttempts);
		Assert.IsTrue(remoteSettings.IsValueCreated);
		Assert.IsTrue(remoteConnection.IsValueCreated);
	}

	#endregion

	#region Cancellation Tests

	[TestMethod]
	public async Task CheckHealthAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		var services = new ServiceCollection();
		using var serviceProvider = services.BuildServiceProvider();
		var healthCheck = new DataProtectionHealthCheck(CreateLoggerMock().Object, serviceProvider);
		using var cancellationSource = new CancellationTokenSource();
		await cancellationSource.CancelAsync();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(
			() => healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellationSource.Token));
	}

	[TestMethod]
	public async Task CheckHealthAsync_CanceledWhilePingIsPending_ThrowsOperationCanceledException()
	{
		var pendingPing = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
		var database = new Mock<IDatabase>();
		database.Setup(x => x.PingAsync(It.IsAny<CommandFlags>())).Returns(pendingPing.Task);
		var connection = new Mock<IConnectionMultiplexer>();
		connection.Setup(x => x.IsConnected).Returns(true);
		connection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

		var services = new ServiceCollection();
		services.AddSingleton(connection.Object);
		services.AddSingleton(CreateDataProtectionMock().Object);
		using var serviceProvider = services.BuildServiceProvider();
		var healthCheck = new DataProtectionHealthCheck(CreateLoggerMock().Object, serviceProvider);
		using var cancellationSource = new CancellationTokenSource();

		var checkTask = healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellationSource.Token);
		await cancellationSource.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() => checkTask);
	}

	[TestMethod]
	public async Task CheckHealthAsync_CanceledWhileRemoteSettingsArePending_ThrowsOperationCanceledException()
	{
		var initializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pendingSettings = new TaskCompletionSource<DataProtectionSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
		var remoteSettings = new AsyncLazy<DataProtectionSettings>(() =>
		{
			initializationStarted.TrySetResult();
			return pendingSettings.Task;
		});
		var services = new ServiceCollection();
		services.AddSingleton(remoteSettings);
		using var serviceProvider = services.BuildServiceProvider();
		var healthCheck = new DataProtectionHealthCheck(CreateLoggerMock().Object, serviceProvider);
		using var cancellationSource = new CancellationTokenSource();

		var checkTask = healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellationSource.Token);
		await initializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await cancellationSource.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() => checkTask);
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
	public async Task CheckHealthAsync_TokenNotCanceled_CompletesSuccessfully()
	{
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
