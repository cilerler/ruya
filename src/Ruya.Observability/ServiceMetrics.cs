using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Ruya.Observability;

public class ServiceMetrics
{
	public static string MeterName = DistributedTracing.AssemblyName;

	public static Meter ApiMeter = new(MeterName, Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0");

	private static readonly Counter<int> HeartbeatTotal = ApiMeter.CreateCounter<int>(
		"heartbeat",
		"total",
		"Total count of heartbeats");

	public static void IncrementHeartbeat()
	{
		HeartbeatTotal.Add(1, new KeyValuePair<string, object?>("service_name", "default"));
	}

	public static void IncrementHeartbeat(string serviceName)
	{
		HeartbeatTotal.Add(1, new KeyValuePair<string, object?>("service_name", serviceName));
	}
}
