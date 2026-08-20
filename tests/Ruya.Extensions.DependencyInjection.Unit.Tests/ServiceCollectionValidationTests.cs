using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Extensions.DependencyInjection.Unit.Tests;

[TestClass]
public sealed class ServiceCollectionValidationTests
{
	[TestMethod]
	public void EnsureServicesRegistered_AllRequiredServicesRegistered_ReturnsSameCollection()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IRequiredService>(new RequiredService());

		var result = services.EnsureServicesRegistered(typeof(IRequiredService));

		Assert.AreSame(services, result);
	}

	[TestMethod]
	public void EnsureServicesRegistered_RequiredServiceMissing_ThrowsDescriptiveInvalidOperationException()
	{
		var services = new ServiceCollection();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(
			() => services.EnsureServicesRegistered(typeof(IRequiredService)));

		StringAssert.Contains(exception.Message, nameof(IRequiredService), StringComparison.Ordinal);
	}

	[TestMethod]
	public void EnsureServicesRegistered_DuplicateMissingService_ReportsServiceOnce()
	{
		var services = new ServiceCollection();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(
			() => services.EnsureServicesRegistered(typeof(IRequiredService), typeof(IRequiredService)));

		Assert.AreEqual(
			exception.Message.IndexOf(nameof(IRequiredService), StringComparison.Ordinal),
			exception.Message.LastIndexOf(nameof(IRequiredService), StringComparison.Ordinal));
	}

	[TestMethod]
	public void EnsureServicesRegistered_NullCollection_ThrowsArgumentNullException()
	{
		IServiceCollection services = null!;

		Assert.ThrowsExactly<ArgumentNullException>(
			() => services.EnsureServicesRegistered(typeof(IRequiredService)));
	}

	[TestMethod]
	public void EnsureServicesRegistered_NullRequiredServicesArray_ThrowsArgumentNullException()
	{
		var services = new ServiceCollection();
		Type[] requiredServices = null!;

		Assert.ThrowsExactly<ArgumentNullException>(
			() => services.EnsureServicesRegistered(requiredServices));
	}

	[TestMethod]
	public void EnsureServicesRegistered_NullRequiredServiceElement_ThrowsArgumentException()
	{
		var services = new ServiceCollection();
		Type[] requiredServices = [null!];

		Assert.ThrowsExactly<ArgumentException>(
			() => services.EnsureServicesRegistered(requiredServices));
	}

	private interface IRequiredService;
	private sealed class RequiredService : IRequiredService;
}
