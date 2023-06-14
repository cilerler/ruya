﻿using System;

namespace Ruya.Bus;

public abstract class BusSettingBase
{
	public const string ConfigurationSectionName = "Bus";

	// QeueueName
	private string _subscriptionClientName;
	public string ConnectionStringKey { get; set; } = "localhost";
	public string UserName { get; set; } = "guest";
	public string Password { get; set; } = "guest";
	public string BrokerName { get; set; } = "bus"; // Exchange Name

	public string SubscriptionClientName
	{
		get => _subscriptionClientName;
		set
		{
			_subscriptionClientName = value;
			if (string.IsNullOrEmpty(_subscriptionClientName)) throw new ArgumentNullException(nameof(SubscriptionClientName));
		}
	}

	public byte RetryCount { get; set; } = default;
}
