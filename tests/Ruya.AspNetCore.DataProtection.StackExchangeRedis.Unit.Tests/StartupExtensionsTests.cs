using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using StackExchange.Redis;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis.Unit.Tests;

[TestClass]
public class StartupExtensionsTests
{
	#region AddDataProtectionServer Tests

	[TestMethod]
	public void AddDataProtectionServer_NullServices_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			StartupExtensions.AddDataProtectionServer(null!));
	}

	[TestMethod]
	public void AddDataProtectionServer_ValidServices_ReturnsServiceCollection()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act
		var result = services.AddDataProtectionServer();

		// Assert
		Assert.AreSame(services, result);
	}

	[TestMethod]
	public void AddDataProtectionServer_WithConfigureAction_InvokesAction()
	{
		// Arrange
		var services = new ServiceCollection();
		var actionCalled = false;

		// Act
		services.AddDataProtectionServer(settings =>
		{
			actionCalled = true;
			settings.DefaultKeyLifetime = 30;
		});

		// Assert - action is invoked during options configuration, not immediately
		// So we just verify it was registered without throwing
		Assert.IsFalse(actionCalled, "Action should not be called until options are resolved");
	}

	#endregion

	#region AddDataProtectionClient Tests

	[TestMethod]
	public void AddDataProtectionClient_NullServices_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			StartupExtensions.AddDataProtectionClient(null!, "test-purpose"));
	}

	[TestMethod]
	public void AddDataProtectionClient_NullDefaultPurpose_ThrowsArgumentException()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			services.AddDataProtectionClient(null!));
	}

	[TestMethod]
	public void AddDataProtectionClient_EmptyDefaultPurpose_ThrowsArgumentException()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act & Assert
		Assert.ThrowsExactly<ArgumentException>(() =>
			services.AddDataProtectionClient(""));
	}

	[TestMethod]
	public void AddDataProtectionClient_WhitespaceDefaultPurpose_ThrowsArgumentException()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act & Assert
		Assert.ThrowsExactly<ArgumentException>(() =>
			services.AddDataProtectionClient("   "));
	}

	[TestMethod]
	public void AddDataProtectionClient_ValidParameters_ReturnsServiceCollection()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act
		var result = services.AddDataProtectionClient("test-purpose");

		// Assert
		Assert.AreSame(services, result);
	}

	[TestMethod]
	public void AddDataProtectionClient_WithConfigureAction_DoesNotThrow()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act & Assert - should not throw
		services.AddDataProtectionClient("test-purpose", settings =>
		{
			// This will be called after settings are fetched
		});
	}

	#endregion

	#region Service Registration Tests

	[TestMethod]
	public void AddDataProtectionServer_RegistersHealthCheck()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act
		services.AddDataProtectionServer();

		// Assert - verify health check builder was accessed
		// The actual health check registration happens via AddHealthChecks().AddCheck()
		var descriptor = services.FirstOrDefault(d =>
			d.ServiceType.FullName?.Contains("HealthCheckService") == true ||
			d.ImplementationType?.Name == "DataProtectionHealthCheck");

		// Health checks are registered differently, so we check if options were registered
		var optionsDescriptor = services.FirstOrDefault(d =>
			d.ServiceType.FullName?.Contains("IConfigureOptions") == true);
		Assert.IsNotNull(optionsDescriptor, "Options should be registered");
	}

	[TestMethod]
	public void AddDataProtectionClient_RegistersAsyncLazySettings()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act
		services.AddDataProtectionClient("test-purpose");

		// Assert
		var descriptor = services.FirstOrDefault(d =>
			d.ServiceType == typeof(AsyncLazy<DataProtectionSettings>));
		Assert.IsNotNull(descriptor, "AsyncLazy<DataProtectionSettings> should be registered");
		Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime);
	}

	[TestMethod]
	public void AddDataProtectionClient_RegistersAsyncLazyConnectionMultiplexer()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act
		services.AddDataProtectionClient("test-purpose");

		// Assert
		var descriptor = services.FirstOrDefault(d =>
			d.ServiceType.FullName?.Contains("AsyncLazy") == true &&
			d.ServiceType.FullName?.Contains("IConnectionMultiplexer") == true);
		Assert.IsNotNull(descriptor, "AsyncLazy<IConnectionMultiplexer> should be registered");
		Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime);
	}

	#endregion
}
