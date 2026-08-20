using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Captures bounded JSON request and response bodies for the current server span.
/// Must be registered after routing but before endpoints.
/// </summary>
public sealed class HttpBodyCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpBodyCapture _capture;

    /// <summary>
    /// Initializes the middleware through the released constructor surface.
    /// </summary>
    public HttpBodyCaptureMiddleware(
        RequestDelegate next,
        IOptions<OpenTelemetrySettings> options)
        : this(next, options, new HttpBodyCapture(options, NullLogger<HttpBodyCapture>.Instance))
    {
    }

    /// <summary>
    /// Initializes the middleware with its shared capture service.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public HttpBodyCaptureMiddleware(
        RequestDelegate next,
        IOptions<OpenTelemetrySettings> options,
        HttpBodyCapture capture)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(capture);

        _next = next;
        _capture = capture;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var activity = Activity.Current;
        if (activity is null || _capture.IsUrlExcluded(context.Request.Path.Value))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (_capture.ShouldCaptureRequest)
        {
            var requestBody = await _capture.CaptureRequestBodyAsync(
                context.Request,
                context.RequestAborted).ConfigureAwait(false);
            if (requestBody is not null)
            {
                activity.SetTag("http.request.body", requestBody);
            }
        }

        if (!_capture.ShouldCaptureResponse)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var originalBody = context.Response.Body;
        await using var captureStream = new BoundedCaptureStream(originalBody, _capture.MaxBodySizeBytes);
        context.Response.Body = captureStream;

        try
        {
            await _next(context).ConfigureAwait(false);

            if (_capture.IsContentTypeAllowed(context.Response.ContentType))
            {
                var responseBody = captureStream.GetCapturedBody(_capture, context.Response.ContentType);
                if (responseBody is not null)
                {
                    activity.SetTag("http.response.body", responseBody);
                }
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
