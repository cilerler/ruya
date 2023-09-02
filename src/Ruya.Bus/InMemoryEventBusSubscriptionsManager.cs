﻿using System;
using System.Collections.Generic;
using System.Linq;
using Ruya.Bus.Abstractions;
using Ruya.Bus.Events;

namespace Ruya.Bus;

public partial class InMemoryEventBusSubscriptionsManager : IEventBusSubscriptionsManager
{
	private readonly List<Type> _eventTypes;


	private readonly Dictionary<string, List<SubscriptionInfo>> _handlers;
	private int _myProperty;

	public InMemoryEventBusSubscriptionsManager()
	{
		_handlers = new Dictionary<string, List<SubscriptionInfo>>();
		_eventTypes = new List<Type>();
	}

	public event EventHandler<string> OnEventRemoved;

	public bool IsEmpty => _handlers is { Count: 0 };

	public void Clear()
	{
		_handlers.Clear();
	}

	public void AddDynamicSubscription<TH>(string eventName)
		where TH : IDynamicIntegrationEventHandler
	{
		DoAddSubscription(typeof(TH), eventName, true);
	}

	public void AddSubscription<T, TH>()
		where T : IntegrationEvent
		where TH : IIntegrationEventHandler<T>
	{
		string eventName = GetEventKey<T>();

		DoAddSubscription(typeof(TH), eventName, false);

		if (!_eventTypes.Contains(typeof(T))) _eventTypes.Add(typeof(T));
	}


	public void RemoveDynamicSubscription<TH>(string eventName)
		where TH : IDynamicIntegrationEventHandler
	{
		SubscriptionInfo handlerToRemove = FindDynamicSubscriptionToRemove<TH>(eventName);
		DoRemoveHandler(eventName, handlerToRemove);
	}


	public void RemoveSubscription<T, TH>()
		where TH : IIntegrationEventHandler<T>
		where T : IntegrationEvent
	{
		SubscriptionInfo handlerToRemove = FindSubscriptionToRemove<T, TH>();
		string eventName = GetEventKey<T>();
		DoRemoveHandler(eventName, handlerToRemove);
	}

	public IEnumerable<SubscriptionInfo> GetHandlersForEvent<T>() where T : IntegrationEvent
	{
		string key = GetEventKey<T>();
		return GetHandlersForEvent(key);
	}

	public IEnumerable<SubscriptionInfo> GetHandlersForEvent(string eventName)
	{
		return _handlers[eventName];
	}

	public bool HasSubscriptionsForEvent<T>() where T : IntegrationEvent
	{
		string key = GetEventKey<T>();
		return HasSubscriptionsForEvent(key);
	}

	public bool HasSubscriptionsForEvent(string eventName)
	{
		return _handlers.ContainsKey(eventName);
	}

	public Type GetEventTypeByName(string eventName)
	{
		return _eventTypes.SingleOrDefault(t => t.Name == eventName);
	}

	public string GetEventKey<T>()
	{
		return typeof(T).Name;
	}

	private void DoAddSubscription(Type handlerType, string eventName, bool isDynamic)
	{
		if (!HasSubscriptionsForEvent(eventName)) _handlers.Add(eventName, new List<SubscriptionInfo>());

		if (_handlers[eventName].Any(s => s.HandlerType == handlerType))
			throw new ArgumentException(
				$"Handler Type {handlerType.Name} already registered for '{eventName}'", nameof(handlerType));

		_handlers[eventName].Add(isDynamic ? SubscriptionInfo.Dynamic(handlerType) : SubscriptionInfo.Typed(handlerType));
	}


	private void DoRemoveHandler(string eventName, SubscriptionInfo subsToRemove)
	{
		if (subsToRemove == null) return;
		_handlers[eventName].Remove(subsToRemove);
		if (_handlers[eventName].Any()) return;
		_handlers.Remove(eventName);
		Type? eventType = _eventTypes.SingleOrDefault(e => e.Name == eventName);
		if (eventType != null) _eventTypes.Remove(eventType);
		RaiseOnEventRemoved(eventName);
	}

	private void RaiseOnEventRemoved(string eventName)
	{
		EventHandler<string>? handler = OnEventRemoved;
		handler?.Invoke(this, eventName);
	}


	private SubscriptionInfo FindDynamicSubscriptionToRemove<TH>(string eventName)
		where TH : IDynamicIntegrationEventHandler
	{
		return DoFindSubscriptionToRemove(eventName, typeof(TH));
	}


	private SubscriptionInfo FindSubscriptionToRemove<T, TH>()
		where T : IntegrationEvent
		where TH : IIntegrationEventHandler<T>
	{
		string eventName = GetEventKey<T>();
		return DoFindSubscriptionToRemove(eventName, typeof(TH));
	}

	private SubscriptionInfo DoFindSubscriptionToRemove(string eventName, Type handlerType)
	{
		if (!HasSubscriptionsForEvent(eventName)) return null;

		return _handlers[eventName].SingleOrDefault(s => s.HandlerType == handlerType);
	}
}
