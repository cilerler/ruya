using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.ReliableMessaging.Extensions;

namespace Ruya.Services.ReliableMessaging.Unit.Tests;

[TestClass]
public sealed class ReliableMessagingOptionsValidationTests
{
	[TestMethod]
	public void Validate_DefaultOptions_Succeeds()
	{
		var result = new ReliableMessagingOptionsValidator().Validate(null, new ReliableMessagingOptions());

		Assert.IsTrue(result.Succeeded);
	}

	[TestMethod]
	public void Validate_NonPositivePollAndBatchValues_Fails()
	{
		var options = new ReliableMessagingOptions();
		options.Outbox.PollInterval = TimeSpan.Zero;
		options.Outbox.BatchSize = 0;

		var result = new ReliableMessagingOptionsValidator().Validate(null, options);

		AssertFailureContains(result, "Outbox:PollInterval");
		AssertFailureContains(result, "Outbox:BatchSize");
	}

	[TestMethod]
	public void Validate_InvalidDurableRetryPolicy_Fails()
	{
		var options = new ReliableMessagingOptions();
		options.Outbox.MaxAttempts = 0;
		options.Outbox.InitialRetryDelay = TimeSpan.FromMinutes(2);
		options.Outbox.MaxRetryDelay = TimeSpan.FromMinutes(1);
		options.Outbox.BackoffFactor = 1;

		var result = new ReliableMessagingOptionsValidator().Validate(null, options);

		AssertFailureContains(result, "Outbox:MaxAttempts");
		AssertFailureContains(result, "Outbox:MaxRetryDelay");
		AssertFailureContains(result, "Outbox:BackoffFactor");
	}

	[TestMethod]
	[DataRow(double.NaN)]
	[DataRow(double.PositiveInfinity)]
	[DataRow(double.NegativeInfinity)]
	public void Validate_NonFiniteBackoffFactor_Fails(double backoffFactor)
	{
		var options = new ReliableMessagingOptions();
		options.Outbox.BackoffFactor = backoffFactor;

		var result = new ReliableMessagingOptionsValidator().Validate(null, options);

		AssertFailureContains(result, "Outbox:BackoffFactor");
	}

	[TestMethod]
	public void Validate_NonPositiveHealthThresholds_Fails()
	{
		var options = new ReliableMessagingOptions();
		options.Outbox.HealthPendingDegradedThreshold = 0;
		options.Outbox.HealthPoisonedUnhealthyThreshold = -1;

		var result = new ReliableMessagingOptionsValidator().Validate(null, options);

		AssertFailureContains(result, "Outbox:HealthPendingDegradedThreshold");
		AssertFailureContains(result, "Outbox:HealthPoisonedUnhealthyThreshold");
	}

	[TestMethod]
	public void Validate_NegativeInboxArchivePeriod_Fails()
	{
		var options = new ReliableMessagingOptions();
		options.Inbox.ArchiveAfter = TimeSpan.FromSeconds(-1);

		var result = new ReliableMessagingOptionsValidator().Validate(null, options);

		AssertFailureContains(result, "Inbox:ArchiveAfter");
	}

	[TestMethod]
	public void Validate_EnabledCleanupWithNonPositiveInterval_Fails()
	{
		var options = new ReliableMessagingOptions();
		options.Inbox.ArchiveAfter = TimeSpan.FromDays(1);
		options.Inbox.CleanupInterval = TimeSpan.Zero;

		var result = new ReliableMessagingOptionsValidator().Validate(null, options);

		AssertFailureContains(result, "Inbox:CleanupInterval");
	}

	[TestMethod]
	public void Validate_DisabledCleanupWithNonPositiveInterval_Succeeds()
	{
		var options = new ReliableMessagingOptions();
		options.Inbox.ArchiveAfter = TimeSpan.Zero;
		options.Inbox.CleanupInterval = TimeSpan.Zero;

		var result = new ReliableMessagingOptionsValidator().Validate(null, options);

		Assert.IsTrue(result.Succeeded);
	}

	[TestMethod]
	public void Validate_BlankDefaultDispatcherName_Fails()
	{
		var options = new ReliableMessagingOptions();
		options.Outbox.DefaultDispatcherName = "   ";

		var result = new ReliableMessagingOptionsValidator().Validate(null, options);

		AssertFailureContains(result, "Outbox:DefaultDispatcherName");
	}

	[TestMethod]
	public void AddReliableMessaging_InvalidOptions_FailsStartupValidation()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
		services.AddReliableMessaging(options => options.Outbox.BatchSize = 0);

		using var provider = services.BuildServiceProvider();
		var startupValidator = provider.GetRequiredService<IStartupValidator>();

		var exception = Assert.ThrowsExactly<OptionsValidationException>(startupValidator.Validate);
		StringAssert.Contains(exception.Message, "Outbox:BatchSize", StringComparison.Ordinal);
	}

	private static void AssertFailureContains(ValidateOptionsResult result, string expected)
	{
		Assert.IsTrue(result.Failed);
		StringAssert.Contains(string.Join("; ", result.Failures), expected, StringComparison.Ordinal);
	}
}
