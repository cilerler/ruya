namespace Ruya.EventBus
{
    public abstract class EventBusSettingBase
    {
        public const string ConfigurationSectionName = "EventBus";
        public string Connection { get; set; } = "localhost"; //ConnectionStringKey
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string BrokerName { get; set; } = "event_bus"; // Exchange Name
        public string SubscriptionClientName { get; set; } // QeueueName
        public byte RetryCount { get; set; } = default(byte);
    }
}
