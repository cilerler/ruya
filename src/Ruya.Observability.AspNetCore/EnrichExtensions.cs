using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace Ruya.Observability.AspNetCore;

public static class EnrichExtensions
{
	private const string StartActivityName = "OnStartActivity";
	private const string StopActivityName = "OnStopActivity";
	private const string RequestTagPrefix = "request";
	private const string ResponseTagPrefix = "response";

	public static void EnrichWithHttpHeaders(this AspNetCoreInstrumentationOptions options)
	{
		options.Enrich = (activity, eventName, rawObject) =>
		{
			if (eventName.Equals(StartActivityName) && rawObject is HttpRequest httpRequest)
			{
				IHeaderDictionary headers = httpRequest.Headers;
				foreach (KeyValuePair<string, StringValues> header in headers)
				foreach (string? value in header.Value)
					activity.SetTag($"{RequestTagPrefix}.{header.Key}", value);
			}
			else if (eventName.Equals(StopActivityName) && rawObject is HttpResponse httpResponse)
			{
				IHeaderDictionary headers = httpResponse.Headers;
				foreach (KeyValuePair<string, StringValues> header in headers)
				foreach (string? value in header.Value)
					activity.SetTag($"{ResponseTagPrefix}.{header.Key}", value);
			}
		};
	}
}
