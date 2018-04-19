using System;
using Newtonsoft.Json;

namespace Ruya.EventBus.RabbitMQ
{
    public class EventBusSetting : EventBusSettingBase
    {
        public ushort PrefetchCount { get; set; } = default(ushort);
        public bool PrefetchCountExists => PrefetchCount.Equals(default(ushort));

        public bool AutomaticRecoveryEnabled { set; get; } = false;
        
        public TimeSpan WaitForConfirmsOrDie { set; get; } = TimeSpan.FromMilliseconds(-1.0);
        [JsonIgnore]
        public bool WaitForConfirmsOrDieExists => !WaitForConfirmsOrDie.Equals(TimeSpan.FromMilliseconds(-1.0));
        
        public string ExchangeType { get; set; } // hard coded always "direct
        public string RoutingKey { get; set; } // notexists based on event class name
        public string Name { get; set; }
        public int MaxQueue { get; set; } //500
    }
}
