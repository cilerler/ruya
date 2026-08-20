using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Ruya.Services.ReliableMessaging;

/// <summary>Validates the polling, cleanup, and durable-retry configuration used by hosted processors.</summary>
public sealed class ReliableMessagingOptionsValidator : IValidateOptions<ReliableMessagingOptions>
{
	public ValidateOptionsResult Validate(string? name, ReliableMessagingOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var failures = new List<string>();
		var outbox = options.Outbox;
		var inbox = options.Inbox;

		if (outbox is null)
		{
			failures.Add("Outbox configuration is required.");
		}
		else
		{
			if (outbox.PollInterval <= TimeSpan.Zero)
			{
				failures.Add("Outbox:PollInterval must be greater than zero.");
			}

			if (outbox.BatchSize <= 0)
			{
				failures.Add("Outbox:BatchSize must be greater than zero.");
			}

			if (outbox.MaxAttempts <= 0)
			{
				failures.Add("Outbox:MaxAttempts must be greater than zero.");
			}

			if (outbox.InitialRetryDelay <= TimeSpan.Zero)
			{
				failures.Add("Outbox:InitialRetryDelay must be greater than zero.");
			}

			if (outbox.MaxRetryDelay < outbox.InitialRetryDelay)
			{
				failures.Add("Outbox:MaxRetryDelay must be greater than or equal to Outbox:InitialRetryDelay.");
			}

			if (!double.IsFinite(outbox.BackoffFactor) || outbox.BackoffFactor <= 1)
			{
				failures.Add("Outbox:BackoffFactor must be finite and greater than one.");
			}

			if (outbox.ArchiveAfter < TimeSpan.Zero)
			{
				failures.Add("Outbox:ArchiveAfter cannot be negative.");
			}

			if (outbox.DefaultDispatcherName is not null &&
				string.IsNullOrWhiteSpace(outbox.DefaultDispatcherName))
			{
				failures.Add("Outbox:DefaultDispatcherName cannot be blank when configured.");
			}

			if (outbox.HealthPendingDegradedThreshold <= 0)
			{
				failures.Add("Outbox:HealthPendingDegradedThreshold must be greater than zero.");
			}

			if (outbox.HealthPoisonedUnhealthyThreshold <= 0)
			{
				failures.Add("Outbox:HealthPoisonedUnhealthyThreshold must be greater than zero.");
			}
		}

		if (inbox is null)
		{
			failures.Add("Inbox configuration is required.");
		}
		else
		{
			if (inbox.ArchiveAfter < TimeSpan.Zero)
			{
				failures.Add("Inbox:ArchiveAfter cannot be negative.");
			}

			if (inbox.ArchiveAfter > TimeSpan.Zero && inbox.CleanupInterval <= TimeSpan.Zero)
			{
				failures.Add("Inbox:CleanupInterval must be greater than zero.");
			}
		}

		return failures.Count == 0
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(failures);
	}
}
