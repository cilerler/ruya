using System.Collections.Generic;
using System.Threading.Tasks;
using Ruya.Bus.Events;

namespace Ruya.Bus.Abstractions
{
    public interface IIntegrationEventHandler<in TIntegrationEvent> : IIntegrationEventHandler where TIntegrationEvent : IntegrationEvent
    {
		//! do not change the property name, there is a hard-coded `.GetMethod("Handle")` exists
		Task Handle(TIntegrationEvent @event, Dictionary<string, object> parameters);
    }

    public interface IIntegrationEventHandler
    {
    }
}
