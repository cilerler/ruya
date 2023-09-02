using System.Diagnostics.Metrics;
using System.Reflection;

namespace Ruya.Observability;

internal class ApiMetrics
{
	public static string MeterName = DistributedTracing.AssemblyName;

	public static Meter ApiMeter = new(MeterName, Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0");

	public static Counter<int> HttpRequestTotal = ApiMeter.CreateCounter<int>(
		"http_request",
		"total",
		"Total count of HTTP requests");

	public static Counter<int> HttpRequestByRouteTotal = ApiMeter.CreateCounter<int>(
		"http_route_request",
		"total",
		"Total count of HTTP route requests");

	public static Counter<int> HttpResponseByStatusTotal = ApiMeter.CreateCounter<int>(
		"http_status_response",
		"total",
		"Total count of HTTP response statuses");
}
