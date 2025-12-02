using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Captures and sanitizes HTTP request/response bodies for tracing.
/// Thread-safe and allocation-optimized.
/// </summary>
public sealed class HttpBodyCapture
{
    private readonly HttpInstrumentationSettings _settings;
    private readonly ILogger<HttpBodyCapture> _logger;
    private readonly ConcurrentDictionary<string, Regex> _excludePatternCache = new();
    private readonly ConcurrentDictionary<string, Regex> _redactPatternCache = new();
    private const string _redactedValue = "[REDACTED]";

    public HttpBodyCapture(
        IOptions<OpenTelemetrySettings> options,
        ILogger<HttpBodyCapture> logger)
    {
        _settings = options.Value.Http;
        _logger = logger;

        // Pre-compile regex patterns
        foreach (var pattern in _settings.ExcludeUrlPatterns)
        {
            _excludePatternCache.TryAdd(pattern, new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
        }
    }

    public bool ShouldCaptureRequest => _settings.CaptureRequestBody;
    public bool ShouldCaptureResponse => _settings.CaptureResponseBody;

    public bool IsUrlExcluded(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        foreach (var pattern in _excludePatternCache.Values)
        {
            if (pattern.IsMatch(path)) return true;
        }
        return false;
    }

    public bool IsContentTypeAllowed(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;

        foreach (var allowed in _settings.AllowedContentTypes)
        {
            if (contentType.Contains(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task<string?> CaptureRequestBodyAsync(HttpRequest request)
    {
        if (!ShouldCaptureRequest) return null;
        if (IsUrlExcluded(request.Path.Value)) return null;
        if (!IsContentTypeAllowed(request.ContentType)) return null;
        if (request.ContentLength > _settings.MaxBodySizeBytes) return "[BODY_TOO_LARGE]";

        try
        {
            request.EnableBuffering();
            request.Body.Position = 0;

            using var reader = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: Math.Min((int)(request.ContentLength ?? 4096), _settings.MaxBodySizeBytes),
                leaveOpen: true);

            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            return SanitizeBody(body, request.ContentType);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to capture request body");
            return "[CAPTURE_ERROR]";
        }
    }

    public async Task<string?> CaptureResponseBodyAsync(HttpResponse response, Stream originalBody)
    {
        if (!ShouldCaptureResponse) return null;
        if (!IsContentTypeAllowed(response.ContentType)) return null;

        try
        {
            if (response.Body is MemoryStream ms)
            {
                if (ms.Length > _settings.MaxBodySizeBytes) return "[BODY_TOO_LARGE]";

                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                ms.Position = 0;

                await ms.CopyToAsync(originalBody);

                return SanitizeBody(body, response.ContentType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to capture response body");
            return "[CAPTURE_ERROR]";
        }

        return null;
    }

    public string? SanitizeBody(string? body, string? contentType)
    {
        if (string.IsNullOrEmpty(body)) return null;

        // Truncate if too long
        if (body.Length > _settings.MaxBodySizeBytes)
        {
            body = body[.._settings.MaxBodySizeBytes] + "...[TRUNCATED]";
        }

        // JSON-specific redaction
        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return SanitizeJson(body);
        }

        return body;
    }

    private string SanitizeJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return json;

            RedactJsonPaths(node, _settings.RedactedJsonPaths);

            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            // Not valid JSON, return as-is
            return json;
        }
    }

    private void RedactJsonPaths(JsonNode node, List<string> paths)
    {
        foreach (var path in paths)
        {
            RedactPath(node, path);
        }
    }

    private void RedactPath(JsonNode? node, string jsonPath)
    {
        if (node is null) return;

        // Simple JSON path implementation: $.field or $.nested.field
        var parts = jsonPath.TrimStart('$', '.').Split('.');

        RedactPathRecursive(node, parts, 0);
    }

    private void RedactPathRecursive(JsonNode? node, string[] parts, int index)
    {
        if (node is null || index >= parts.Length) return;

        var currentPart = parts[index];
        var isLastPart = index == parts.Length - 1;

        switch (node)
        {
            case JsonObject obj:
                if (isLastPart)
                {
                    // Case-insensitive key matching
                    var keyToRedact = obj.AsObject()
                        .Select(kvp => kvp.Key)
                        .FirstOrDefault(k => k.Equals(currentPart, StringComparison.OrdinalIgnoreCase));

                    if (keyToRedact is not null)
                    {
                        obj[keyToRedact] = _redactedValue;
                    }
                }
                else
                {
                    var child = obj.AsObject()
                        .FirstOrDefault(kvp => kvp.Key.Equals(currentPart, StringComparison.OrdinalIgnoreCase))
                        .Value;
                    RedactPathRecursive(child, parts, index + 1);
                }

                // Also recurse into all children for nested objects
                foreach (var child in obj.AsObject().Select(kvp => kvp.Value))
                {
                    RedactPathRecursive(child, parts, 0);
                }
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    RedactPathRecursive(item, parts, index);
                }
                break;
        }
    }

    public Dictionary<string, string> SanitizeHeaders(IHeaderDictionary headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            var value = _settings.RedactedHeaders
                .Any(h => h.Equals(header.Key, StringComparison.OrdinalIgnoreCase))
                ? _redactedValue
                : header.Value.ToString();

            result[header.Key] = value;
        }

        return result;
    }
}
