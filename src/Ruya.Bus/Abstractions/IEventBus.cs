using System.Collections.Generic;
using Ruya.Bus.Events;

namespace Ruya.Bus.Abstractions
{
    public interface IEventBus
    {
        void Publish(IntegrationEvent @event, Dictionary<string, object> parameters);

        void Subscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>;

		void SubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler;

		void UnsubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler;

		void Unsubscribe<T, TH>() where TH : IIntegrationEventHandler<T> where T : IntegrationEvent;

		void AddConsumerChannel(string queueName);
		void RemoveConsumerChannel(string queueName);
    }
}
