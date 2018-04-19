using System;

namespace Ruya.Services.MessageQueue.Abstractions
{
	public interface IMessageQueueSettings
	{
		string Name { get; set; }
		string ConnectionStringKey { set; get; }
		string Exchange { set; get; }
		string ExchangeType { set; get; }
		string Queue { set; get; }
		string RoutingKey { set; get; }
		ushort PrefetchCount { set; get; }
		int MaxQueue { set; get; }
		bool AutomaticRecoveryEnabled { set; get; }
		TimeSpan RequestedHeartbeatSeconds { set; get; }
	}
}
