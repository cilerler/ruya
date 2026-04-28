using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>
/// Health check for the outbox of a specific persistence context. Reports:
/// <list type="bullet">
///   <item><description><see cref="HealthStatus.Unhealthy"/> when poisoned count &#x2265; <see cref="OutboxOptions.HealthPoisonedUnhealthyThreshold"/>.</description></item>
///   <item><description><see cref="HealthStatus.Degraded"/> when pending count &#x2265; <see cref="OutboxOptions.HealthPendingDegradedThreshold"/>.</description></item>
///   <item><description><see cref="HealthStatus.Healthy"/> otherwise.</description></item>
/// </list>
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context (typically the caller's <c>DbContext</c>).</typeparam>
public sealed class OutboxHealthCheck<TContext> : IHealthCheck
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly OutboxOptions _options;

	public OutboxHealthCheck(IServiceScopeFactory scopeFactory, IOptions<ReliableMessagingOptions> options)
	{
		ArgumentNullException.ThrowIfNull(scopeFactory);
		ArgumentNullException.ThrowIfNull(options);
		_scopeFactory = scopeFactory;
		_options = options.Value.Outbox;
	}

	public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
	{
		using var scope = _scopeFactory.CreateScope();
		var store = scope.ServiceProvider.GetRequiredService<IOutboxStore<TContext>>();

		var pending = await store.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
		var poisoned = await store.GetPoisonedCountAsync(cancellationToken).ConfigureAwait(false);

		var data = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["pending"] = pending,
			["poisoned"] = poisoned,
			["pending_degraded_threshold"] = _options.HealthPendingDegradedThreshold,
			["poisoned_unhealthy_threshold"] = _options.HealthPoisonedUnhealthyThreshold,
		};

		if (poisoned >= _options.HealthPoisonedUnhealthyThreshold)
		{
			return HealthCheckResult.Unhealthy(
				$"Outbox has {poisoned} poisoned entry(ies) — manual intervention required.",
				data: data);
		}

		if (pending >= _options.HealthPendingDegradedThreshold)
		{
			return HealthCheckResult.Degraded(
				$"Outbox backlog building: {pending} pending entry(ies).",
				data: data);
		}

		return HealthCheckResult.Healthy(
			$"Outbox healthy ({pending} pending, {poisoned} poisoned).",
			data: data);
	}
}
