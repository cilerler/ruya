using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Middleware that captures HTTP response bodies for tracing.
/// Must be registered after routing but before endpoints.
/// </summary>
public sealed class HttpBodyCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpInstrumentationSettings _settings;

    public HttpBodyCaptureMiddleware(
        RequestDelegate next,
        IOptions<OpenTelemetrySettings> options)
    {
        _next = next;
        _settings = options.Value.Http;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip if response capture disabled or path excluded
        if (!_settings.CaptureResponseBody || IsPathExcluded(context.Request.Path.Value))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;

        try
        {
            using var memoryStream = new MemoryStream();
            context.Response.Body = memoryStream;

            await _next(context);

            // Capture response body
            if (Activity.Current is not null && ShouldCaptureResponse(context.Response))
            {
                memoryStream.Position = 0;
                var body = await CaptureBodyAsync(memoryStream);
                
                if (!string.IsNullOrEmpty(body))
                {
                    Activity.Current.SetTag("http.response.body", body);
                }
            }

            // Copy back to original stream
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBody);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private bool IsPathExcluded(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        return _settings.ExcludeUrlPatterns.Any(pattern =>
            path.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldCaptureResponse(HttpResponse response)
    {
        var contentType = response.ContentType;
        if (string.IsNullOrEmpty(contentType)) return false;

        return _settings.AllowedContentTypes.Any(t =>
            contentType.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> CaptureBodyAsync(MemoryStream stream)
    {
        if (stream.Length == 0) return null;

        if (stream.Length > _settings.MaxBodySizeBytes)
        {
            return "[BODY_TOO_LARGE]";
        }

        try
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            var body = await reader.ReadToEndAsync();

            return SanitizeBody(body);
        }
        catch
        {
            return "[CAPTURE_ERROR]";
        }
    }

    private string SanitizeBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return body;

        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(body);
            if (node is null) return body;

            foreach (var path in _settings.RedactedJsonPaths)
            {
                RedactPath(node, path.TrimStart('$', '.').Split('.'), 0);
            }

            return node.ToJsonString();
        }
        catch
        {
            return body;
        }
    }

    private static void RedactPath(System.Text.Json.Nodes.JsonNode? node, string[] parts, int index)
    {
        if (node is null || index >= parts.Length) return;

        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            var key = obj.AsObject()
                .Select(kvp => kvp.Key)
                .FirstOrDefault(k => k.Equals(parts[index], StringComparison.OrdinalIgnoreCase));

            if (key is not null)
            {
                if (index == parts.Length - 1)
                {
                    obj[key] = "[REDACTED]";
                }
                else
                {
                    RedactPath(obj[key], parts, index + 1);
                }
            }

            foreach (var child in obj.AsObject().Select(kvp => kvp.Value))
            {
                RedactPath(child, parts, 0);
            }
        }
        else if (node is System.Text.Json.Nodes.JsonArray arr)
        {
            foreach (var item in arr)
            {
                RedactPath(item, parts, index);
            }
        }
    }
}
