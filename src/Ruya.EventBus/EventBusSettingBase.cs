using System;

namespace Ruya.EventBus
{
    public abstract class EventBusSettingBase
    {
	    public const string ConfigurationSectionName = "EventBus";
        public string Connection { get; set; } = "localhost"; //ConnectionStringKey
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string BrokerName { get; set; } = "event_bus"; // Exchange Name

		// QeueueName
	    private string _subscriptionClientName;
		public string SubscriptionClientName
		{
			get => _subscriptionClientName;
			set
			{
				_subscriptionClientName = value;
				if (string.IsNullOrEmpty(_subscriptionClientName))
				{
					throw new ArgumentNullException(nameof(SubscriptionClientName));
				}
			}
		}

	    public byte RetryCount { get; set; } = default(byte);
    }
}
