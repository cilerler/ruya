using System.Collections.Generic;

namespace Ruya.Observability.Middlewares;

public static class MetricsHeader
{
	public const string ClientInfo = "X-Client-Info";
	public const string ApplicationName = "X-Application-Name";

	public static Dictionary<string, string> RequestHeaderLabelMap = new()
	{
		{ ApplicationName, "application_name" }, { ClientInfo, "client_info" }
	};
}
