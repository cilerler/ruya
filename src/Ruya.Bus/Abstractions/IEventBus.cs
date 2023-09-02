using System;
using Ruya.Bus.Events;

namespace Ruya.Bus.Abstractions;

public interface IEventBus
{
	void Publish(IntegrationEvent @event, byte priority = default, TimeSpan delay = default);

	void Subscribe<T, TH>()
		where T : IntegrationEvent
		where TH : IIntegrationEventHandler<T>;

	void Unsubscribe<T, TH>()
		where TH : IIntegrationEventHandler<T>
		where T : IntegrationEvent;
}
