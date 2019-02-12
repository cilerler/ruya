using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ruya.Bus.RabbitMQ
{
    public class BusSetting : BusSettingBase
    {
	    public string VirtualHost { get; set; } = "/";
	    public bool AutomaticRecoveryEnabled { get; set; }
		public string AppId { set; get; }
	    public TimeSpan RequestedHeartbeatSeconds { get; set; }
        public TimeSpan WaitForConfirmsOrDie { set; get; } = TimeSpan.FromMilliseconds(-1.0);
        [JsonIgnore]
        public bool WaitForConfirmsOrDieExists => !WaitForConfirmsOrDie.Equals(TimeSpan.FromMilliseconds(-1.0));
        public bool EnableDeclarations { set; get; }
        public List<Exchange> Exchanges { set; get; }
        public List<Queue> Queues { set; get; }
        public List<Binding> Bindings { set; get; }
		public bool PrefetchCountExists => PrefetchCount.Equals(default);
		public ushort PrefetchCount { set; get; } = default;
		public int MaxQueue { set; get; } //500
    }
}
