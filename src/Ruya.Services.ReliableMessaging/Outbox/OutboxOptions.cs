using System;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>Producer-side options governing the outbox processor's behaviour.</summary>
/// <remarks>
/// <para>
/// The outbox has two distinct retry layers:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <b>In-process transient retry</b> inside a single poll iteration, driven by a Polly
///       <c>ResiliencePipeline</c>. Registered in DI under
///       <see cref="OutboxResiliencePipelineKey.Dispatch"/>; override with your own
///       <c>AddResiliencePipeline</c> call using the same key to change the policy. Default: 3 retries
///       with exponential backoff + jitter over a few seconds.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Cross-restart durable retry</b> persisted on <see cref="OutboxEntry"/> via
///       <see cref="OutboxEntry.AttemptCount"/> and <see cref="OutboxEntry.NextAttemptAt"/>. Governed by the
///       three scalars below (<see cref="InitialRetryDelay"/>, <see cref="MaxRetryDelay"/>,
///       <see cref="BackoffFactor"/>). This layer catches longer outages that span process restarts.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class OutboxOptions
{
	/// <summary>How often the processor polls the outbox store for pending entries.</summary>
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

	/// <summary>Maximum number of entries fetched (and attempted) per poll iteration.</summary>
	public int BatchSize { get; set; } = 100;

	/// <summary>After this many durable-retry cycles, an entry is marked <see cref="OutboxStatus.Poisoned"/>.</summary>
	public int MaxAttempts { get; set; } = 10;

	/// <summary>Delay used for the first durable retry after Polly's in-process pipeline exhausts.</summary>
	public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>Upper bound on the durable-retry delay.</summary>
	public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

	/// <summary>Multiplicative backoff factor applied to successive durable retries.</summary>
	public double BackoffFactor { get; set; } = 2.0;

	/// <summary>
	/// How long successfully dispatched entries may remain in the store before being eligible for archival / deletion
	/// by a maintenance task. <see cref="TimeSpan.Zero"/> disables automatic cleanup.
	/// </summary>
	public TimeSpan ArchiveAfter { get; set; } = TimeSpan.FromDays(7);

	/// <summary>
	/// Dispatcher name to stamp on envelopes that do not specify one. Interpreted by the active
	/// <see cref="IOutboundDispatcher"/> implementation (e.g. the MessageQueue provider name).
	/// </summary>
	public string? DefaultDispatcherName { get; set; }

	/// <summary>
	/// When the outbox pending count reaches this value, the health check reports
	/// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>. Default: 1000.
	/// </summary>
	public long HealthPendingDegradedThreshold { get; set; } = 1000;

	/// <summary>
	/// When the outbox poisoned count reaches this value, the health check reports
	/// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy"/>. Default: 1 (any poison fails health).
	/// </summary>
	public long HealthPoisonedUnhealthyThreshold { get; set; } = 1;
}

/// <summary>Polly <c>ResiliencePipeline</c> keys used by the outbox.</summary>
public static class OutboxResiliencePipelineKey
{
	/// <summary>
	/// Key for the in-process retry / circuit-breaker policy wrapping <see cref="IOutboundDispatcher.DispatchAsync"/>.
	/// Override by registering a pipeline under the same key via
	/// <c>services.AddResiliencePipeline(OutboxResiliencePipelineKey.Dispatch, builder =&gt; ...)</c>.
	/// The string is derived from the type's full name + member name so the literal namespace is not duplicated in source.
	/// </summary>
	public static readonly string Dispatch =
		(typeof(OutboxResiliencePipelineKey).FullName
			?? throw new InvalidOperationException("The outbox resilience-pipeline key type must have a full name."))
		+ "." + nameof(Dispatch);
}
