using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using Ruya.Primitives;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// OpenTelemetry-compatible metrics for consumer-side idempotency.
/// Registered as a singleton by <c>AddReliableMessaging</c>; consumed by the MessageQueue adapter's
/// subscribe wrapper and by <see cref="InboxCleanupProcessor{TContext}"/>.
/// </summary>
public sealed class InboxMetrics : IDisposable
{
	private readonly Meter _meter;
	private readonly Counter<long> _received;
	private readonly Counter<long> _processed;
	private readonly Counter<long> _cleanupRemoved;

	public InboxMetrics(IMeterFactory meterFactory)
	{
		ArgumentNullException.ThrowIfNull(meterFactory);

		_meter = meterFactory.Create(new MeterOptions(Startup.AssemblyName)
		{
			Version = Startup.AssemblyVersion,
			Tags = new TagList
			{
				{ "code.namespace", GetType().Namespace },
				{ "code.class", GetType().Name }
			}
		});
		_received = _meter.CreateCounter<long>("inbox.received_total", unit: "{message}");
		_processed = _meter.CreateCounter<long>("inbox.processed_total", unit: "{message}");
		_cleanupRemoved = _meter.CreateCounter<long>("inbox.cleanup_removed_total", unit: "{message}");
	}

	/// <summary>Records one inbound message observation. <paramref name="outcome"/> is <c>"first"</c> or <c>"duplicate"</c>.</summary>
	public void RecordReceived(string consumerName, string topic, string outcome)
	{
		_received.Add(1,
			new KeyValuePair<string, object?>("consumer", consumerName),
			new KeyValuePair<string, object?>("topic", topic),
			new KeyValuePair<string, object?>("outcome", outcome));
	}

	/// <summary>Records one message marked processed by its handler.</summary>
	public void RecordProcessed(string consumerName, string topic)
	{
		_processed.Add(1,
			new KeyValuePair<string, object?>("consumer", consumerName),
			new KeyValuePair<string, object?>("topic", topic));
	}

	/// <summary>Records a batch of rows removed by the cleanup processor.</summary>
	public void RecordCleanup(int removed)
	{
		if (removed > 0)
		{
			_cleanupRemoved.Add(removed);
		}
	}

	public void Dispose()
	{
		_meter.Dispose();
	}
}
