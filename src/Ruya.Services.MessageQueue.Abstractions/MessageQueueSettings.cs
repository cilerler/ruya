using System;

namespace Ruya.Services.MessageQueue.Abstractions
{
    public class MessageQueueSettings : IMessageQueueSettings
    {
        public string ConnectionStringKey { get; set; }
        public string Name { get; set; }
        public string Exchange { get; set; }
        public string ExchangeType { get; set; }
        public string Queue { get; set; }
        public string RoutingKey { get; set; }
        public ushort PrefetchCount { get; set; }
        public int MaxQueue { get; set; }

        public bool AutomaticRecoveryEnabled { set; get; }
        public TimeSpan RequestedHeartbeatSeconds { set; get; }
    }
}
