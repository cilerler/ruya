using System;
using System.Collections.Generic;

namespace Ruya.Services.ReliableMessaging.Outbox;

/// <summary>Default in-memory buffer. Registered as scoped so each DI scope gets its own instance.</summary>
public sealed class OutboxBuffer<TContext> : IOutboxBuffer<TContext>
{
	private readonly List<ReliableMessageEnvelope> _items = new();
	private readonly object _sync = new();

	public void Add(ReliableMessageEnvelope envelope)
	{
		ArgumentNullException.ThrowIfNull(envelope);
		lock (_sync)
		{
			_items.Add(envelope);
		}
	}

	public IReadOnlyList<ReliableMessageEnvelope> Drain()
	{
		lock (_sync)
		{
			if (_items.Count == 0)
			{
				return [];
			}

			var snapshot = _items.ToArray();
			_items.Clear();
			return snapshot;
		}
	}

	public int Count
	{
		get
		{
			lock (_sync)
			{
				return _items.Count;
			}
		}
	}
}
