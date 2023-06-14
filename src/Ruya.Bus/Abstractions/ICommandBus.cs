using System.Threading.Tasks;
using Ruya.Bus.Commands;

namespace Ruya.Bus.Abstractions;

public interface ICommandBus
{
	Task SendAsync<T>(T command) where T : IntegrationCommand;
	void Send<T>(string name, T data);
	void Handle<TC>(string name, IIntegrationCommandHandler<TC> handler);
	void Handle(string name, IIntegrationCommandHandler handler);
	void Handle<TI, TC>(TI handler) where TI : IIntegrationCommandHandler<TC>;
}
