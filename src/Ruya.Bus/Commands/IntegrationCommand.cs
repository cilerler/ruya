using System;

namespace Ruya.Bus.Commands
{
	public abstract class IntegrationCommand
	{
		protected IntegrationCommand()
		{
			Id = Guid.NewGuid();
			Sent = DateTime.UtcNow;
		}

		public Guid Id { get; }
		public DateTime Sent { get; }
		public abstract object GetDataAsObject();
	}

	public class IntegrationCommand<T> : IntegrationCommand
	{
		public IntegrationCommand(string name, T data)
		{
			Data = data;
			Name = name;
		}

		public T Data { get; }
		public string Name { get; }
		public override object GetDataAsObject() => Data;
	}
}
