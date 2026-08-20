using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

using Ruya.Primitives;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>
/// Hosted service that drains the outbox for a specific persistence context.
/// Polls <see cref="IOutboxStore{TContext}"/>, dispatches via <see cref="IOutboundDispatcher"/> wrapped in a Polly
/// <see cref="ResiliencePipeline"/> for transient in-process retries, and records results.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context (typically the caller's <c>DbContext</c>).</typeparam>
public sealed partial class OutboxProcessor<TContext> : BackgroundService
{
	private static readonly ActivitySource _activitySource = new(Startup.AssemblyName, Startup.AssemblyVersion);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IOutboundDispatcher _dispatcher;
	private readonly ResiliencePipeline _dispatchPipeline;
	private readonly ILogger<OutboxProcessor<TContext>> _logger;
	private readonly OutboxOptions _options;

	private readonly Counter<long> _dispatched;
	private readonly Counter<long> _failures;
	private readonly Histogram<double> _duration;

	public OutboxProcessor(
		IServiceScopeFactory scopeFactory,
		IOutboundDispatcher dispatcher,
		ResiliencePipelineProvider<string> pipelineProvider,
		IOptions<ReliableMessagingOptions> options,
		IMeterFactory meterFactory,
		ILogger<OutboxProcessor<TContext>> logger)
	{
		ArgumentNullException.ThrowIfNull(scopeFactory);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(pipelineProvider);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(meterFactory);
		ArgumentNullException.ThrowIfNull(logger);

		_scopeFactory = scopeFactory;
		_dispatcher = dispatcher;
		_dispatchPipeline = pipelineProvider.GetPipeline(OutboxResiliencePipelineKey.Dispatch);
		_logger = logger;
		_options = options.Value.Outbox;

		var meter = meterFactory.Create(new MeterOptions(Startup.AssemblyName)
		{
			Version = Startup.AssemblyVersion,
			Tags = new TagList
			{
				{ "code.namespace", GetType().Namespace },
				{ "code.class", GetType().Name }
			}
		});
		_dispatched = meter.CreateCounter<long>("outbox.dispatched_total", unit: "{envelope}");
		_failures = meter.CreateCounter<long>("outbox.failures_total", unit: "{failure}");
		_duration = meter.CreateHistogram<double>("outbox.dispatch_duration", unit: "ms");
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			// Delay-first ordering: avoids racing the host's startup (SQL connection pool warmup, EF Core
			// first-time query compilation) on the first iteration. The cost is one PollInterval of latency
			// before the very first dispatch attempt, which matters less than producing a startup error log.
			try
			{
				await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}

			try
			{
				await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
#pragma warning disable CA1031 // top-level processor must not crash the host; log and back off
			catch (Exception ex)
			{
				LogProcessorIteration(ex);
			}
#pragma warning restore CA1031
		}
	}

	private async Task ProcessBatchAsync(CancellationToken ct)
	{
		using var scope = _scopeFactory.CreateScope();
		var store = scope.ServiceProvider.GetRequiredService<IOutboxStore<TContext>>();

		var pending = await store.FetchPendingAsync(_options.BatchSize, ct).ConfigureAwait(false);
		if (pending.Count == 0)
		{
			return;
		}

		foreach (var entry in pending)
		{
			if (ct.IsCancellationRequested)
			{
				break;
			}

			using var activity = _activitySource.StartActivity("Outbox.Dispatch", ActivityKind.Producer);
			activity?.SetTag("outbox.topic", entry.Topic);
			activity?.SetTag("outbox.message_id", entry.Id);
			activity?.SetTag("outbox.attempt", entry.AttemptCount + 1);

			var stopwatch = Stopwatch.StartNew();
			try
			{
				var envelope = ToEnvelope(entry);

				await _dispatchPipeline.ExecuteAsync(
					async token => await _dispatcher.DispatchAsync(envelope, token).ConfigureAwait(false),
					ct).ConfigureAwait(false);

				await store.MarkDispatchedAsync(entry, ct).ConfigureAwait(false);

				stopwatch.Stop();
				_duration.Record(stopwatch.Elapsed.TotalMilliseconds);
				_dispatched.Add(1);
				activity?.SetStatus(ActivityStatusCode.Ok);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
#pragma warning disable CA1031 // per-envelope failure handling; must not abort the batch
			catch (Exception ex)
			{
				stopwatch.Stop();
				_failures.Add(1);
				activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

				var newAttemptCount = entry.AttemptCount + 1;
				if (newAttemptCount >= _options.MaxAttempts)
				{
					LogPoisoned(entry.Id, entry.Topic, newAttemptCount, ex);
					await store.MarkPoisonedAsync(entry, ex.Message, ct).ConfigureAwait(false);
				}
				else
				{
					var delay = GetDurableRetryDelay(newAttemptCount);
					LogRetryScheduled(entry.Id, entry.Topic, newAttemptCount, delay, ex);
					await store.ScheduleRetryAsync(entry, ex.Message, DateTime.UtcNow + delay, ct).ConfigureAwait(false);
				}
			}
#pragma warning restore CA1031
		}
	}

	private TimeSpan GetDurableRetryDelay(int attemptNumber)
	{
		// Polly handles the in-process pipeline. This schedule is the cross-restart retry for longer outages.
		var factor = _options.BackoffFactor <= 0 ? 2.0 : _options.BackoffFactor;
		var exponentialSeconds = _options.InitialRetryDelay.TotalSeconds * Math.Pow(factor, Math.Max(0, attemptNumber - 1));
		var computed = TimeSpan.FromSeconds(exponentialSeconds);
		return computed > _options.MaxRetryDelay ? _options.MaxRetryDelay : computed;
	}

	private static ReliableMessageEnvelope ToEnvelope(OutboxEntry entry)
	{
		var headers = string.IsNullOrWhiteSpace(entry.HeadersJson)
			? null
			: JsonSerializer.Deserialize<Dictionary<string, string>>(entry.HeadersJson)
				?? throw new InvalidOperationException($"Outbox entry '{entry.Id}' headers deserialized to null.");

		return new ReliableMessageEnvelope
		{
			MessageId = entry.Id,
			Topic = entry.Topic,
			DispatcherName = entry.DispatcherName,
			PayloadJson = entry.PayloadJson,
			PayloadType = entry.PayloadType,
			EnqueuedAt = entry.EnqueuedAt,
			Headers = headers,
		};
	}

	[LoggerMessage(EventId = 5001, Level = LogLevel.Error,
		Message = "Outbox processor iteration failed")]
	private partial void LogProcessorIteration(Exception exception);

	[LoggerMessage(EventId = 5002, Level = LogLevel.Warning,
		Message = "Outbox entry {MessageId} ({Topic}) attempt {Attempt} failed; retrying in {Delay}.")]
	private partial void LogRetryScheduled(Guid messageId, string topic, int attempt, TimeSpan delay, Exception exception);

	[LoggerMessage(EventId = 5003, Level = LogLevel.Error,
		Message = "Outbox entry {MessageId} ({Topic}) poisoned after {Attempt} attempts.")]
	private partial void LogPoisoned(Guid messageId, string topic, int attempt, Exception exception);
}
