using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Ruya.Observability.Middlewares;

public class RequestMetricsMiddleware
{
	private readonly RequestDelegate _next;

	public RequestMetricsMiddleware(RequestDelegate next)
	{
		_next = next;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var requestTags = new TagList();
		var responseTags = new TagList();

		foreach (KeyValuePair<string, string> kv in MetricsHeader.RequestHeaderLabelMap)
			if (context.Request.Headers.ContainsKey(kv.Key))
			{
				StringValues headerValues = context.Request.Headers[kv.Key];
				string? header = headerValues.FirstOrDefault();
				if (header != null)
				{
					requestTags.Add(kv.Value, header);
					responseTags.Add(kv.Value, header);
				}
			}

		PathString route = context.Request.Path;
		// Gets around odata route naming messing up the metric e.g `ImportQueue(16)`
		string fixedRoute = route.ToString().Split('(', 2)[0];
		requestTags.Add("route", fixedRoute);
		requestTags.Add("method", context.Request.Method);

		ApiMetrics.HttpRequestByRouteTotal.Add(1, requestTags);
		await _next(context);

		responseTags.Add("status", context.Response.StatusCode.ToString());
		ApiMetrics.HttpResponseByStatusTotal.Add(1, responseTags);
	}
}
