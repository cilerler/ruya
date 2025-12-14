using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Primitives;

namespace Ruya.AspNetCore.Middleware.AppEnvironmentResponseHeaders;

/// <summary>
/// Middleware that adds application environment information to response headers.
/// </summary>
public sealed class AppEnvironmentResponseHeadersMiddleware
{
	private readonly RequestDelegate _next;
	private readonly ILogger<AppEnvironmentResponseHeadersMiddleware> _logger;
	private readonly AppEnvironmentResponseHeadersSettings _settings;
	private readonly List<KeyValuePair<string, string>> _staticHeaders;

	/// <summary>
	/// Initializes a new instance of the <see cref="AppEnvironmentResponseHeadersMiddleware"/> class.
	/// </summary>
	/// <param name="next">The next middleware in the pipeline.</param>
	/// <param name="logger">The logger instance.</param>
	/// <param name="options">The configuration options.</param>
	public AppEnvironmentResponseHeadersMiddleware(
		RequestDelegate next,
		ILogger<AppEnvironmentResponseHeadersMiddleware> logger,
		IOptions<AppEnvironmentResponseHeadersSettings> options)
	{
		ArgumentNullException.ThrowIfNull(next);
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(options);

		_next = next;
		_logger = logger;
		_settings = options.Value;

		_staticHeaders = BuildStaticHeaders();
	}

	/// <summary>
	/// Invokes the middleware.
	/// </summary>
	/// <param name="httpContext">The HTTP context.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	public Task InvokeAsync(HttpContext httpContext)
	{
		ArgumentNullException.ThrowIfNull(httpContext);

		if (!_settings.Enabled)
		{
			_logger.MiddlewareDisabled();
			return _next(httpContext);
		}

		var headers = httpContext.Response.Headers;
		foreach (var header in _staticHeaders)
		{
			headers[header.Key] = header.Value;
		}

		_logger.HeadersAdded(_staticHeaders.Count);

		return _next(httpContext);
	}

	private List<KeyValuePair<string, string>> BuildStaticHeaders()
	{
		var headers = new List<KeyValuePair<string, string>>(capacity: 4);

		if (_settings.IncludeVersion)
		{
			headers.Add(new KeyValuePair<string, string>("X-ApplicationVersion", Startup.AssemblyVersion));
		}

		if (_settings.IncludeName)
		{
			headers.Add(new KeyValuePair<string, string>("X-ApplicationName", Startup.AssemblyName));
		}

		if (_settings.IncludeEnvironment)
		{
			headers.Add(new KeyValuePair<string, string>("X-Environment", Startup.EnvironmentName));
		}

		if (_settings.IncludeMachineName)
		{
			headers.Add(new KeyValuePair<string, string>("X-MachineName", Environment.MachineName));
		}

		return headers;
	}
}
