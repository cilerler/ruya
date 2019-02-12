using System.Threading.Tasks;

namespace Ruya.Bus.Abstractions
{
    public interface IDynamicIntegrationEventHandler
    {
        Task Handle(dynamic eventData);
    }
}
