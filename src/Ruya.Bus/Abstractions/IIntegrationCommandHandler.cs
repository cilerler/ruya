using Ruya.Bus.Commands;

namespace Ruya.Bus.Abstractions
{
	public interface IIntegrationCommandHandler
	{
		void Handle(IntegrationCommand command);
	}

	public interface IIntegrationCommandHandler<T> : IIntegrationCommandHandler
	{
		void Handle(IntegrationCommand<T> command);
	}
}
