using System.Collections.Generic;
using System.Threading.Tasks;
using Ruya.Bus.Abstractions;

namespace Ruya.Bus.Tests
{
    public class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public bool Handled { get; private set; }

        public TestIntegrationEventHandler()
        {
            Handled = false;
        }

        public async Task Handle(TestIntegrationEvent @event, Dictionary<string, object> parameters) => Handled = true;
    }
}
