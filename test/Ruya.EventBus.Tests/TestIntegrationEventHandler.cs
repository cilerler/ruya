using System.Threading.Tasks;
using Ruya.EventBus.Abstractions;

namespace Ruya.EventBus.Tests
{
    public class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public bool Handled { get; private set; }

        public TestIntegrationEventHandler()
        {
            Handled = false;
        }

        public async Task Handle(TestIntegrationEvent @event) => Handled = true;
    }
}
