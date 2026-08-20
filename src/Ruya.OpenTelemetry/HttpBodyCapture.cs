using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Captures bounded JSON request and response bodies for tracing.
/// </summary>
public sealed class HttpBodyCapture
{
    internal const string BodyTooLarge = "[BODY_TOO_LARGE]";
    internal const string CaptureError = "[CAPTURE_ERROR]";
    internal const string InvalidJson = "[INVALID_JSON_BODY]";
    internal const string NonJson = "[NON_JSON_BODY_NOT_CAPTURED]";

    private const string RedactedValue = "[REDACTED]";
    private readonly HttpInstrumentationSettings _settings;
    private readonly ILogger<HttpBodyCapture> _logger;
    private readonly UrlExclusionMatcher _urlExclusions;

    public HttpBodyCapture(
        IOptions<OpenTelemetrySettings> options,
        ILogger<HttpBodyCapture> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = options.Value.Http;
        _logger = logger;

        _urlExclusions = new UrlExclusionMatcher(_settings.ExcludeUrlPatterns);
    }

    public bool ShouldCaptureRequest => _settings.CaptureRequestBody;
    public bool ShouldCaptureResponse => _settings.CaptureResponseBody;

    internal int MaxBodySizeBytes => _settings.MaxBodySizeBytes;

    public bool IsUrlExcluded(string? path)
    {
        return _urlExclusions.IsExcluded(path);
    }

    public bool IsContentTypeAllowed(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType) ||
            !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _settings.AllowedContentTypes.Any(allowed =>
            contentType.Contains(allowed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Captures a request body using the request-aborted token.
    /// </summary>
    public Task<string?> CaptureRequestBodyAsync(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CaptureRequestBodyAsync(request, request.HttpContext.RequestAborted);
    }

    /// <summary>
    /// Captures a request body while observing both caller cancellation and request abort.
    /// </summary>
    public async Task<string?> CaptureRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            request.HttpContext.RequestAborted);
        var effectiveToken = linkedCancellation.Token;
        effectiveToken.ThrowIfCancellationRequested();

        if (!ShouldCaptureRequest || IsUrlExcluded(request.Path.Value) || !IsContentTypeAllowed(request.ContentType))
        {
            return null;
        }

        if (request.ContentLength > _settings.MaxBodySizeBytes)
        {
            return BodyTooLarge;
        }

        request.EnableBuffering();
        var buffer = ArrayPool<byte>.Shared.Rent(_settings.MaxBodySizeBytes + 1);

        try
        {
            request.Body.Position = 0;
            var bytesRead = 0;

            while (bytesRead <= _settings.MaxBodySizeBytes)
            {
                var read = await request.Body.ReadAsync(
                    buffer.AsMemory(bytesRead, _settings.MaxBodySizeBytes + 1 - bytesRead),
                    effectiveToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            if (bytesRead > _settings.MaxBodySizeBytes)
            {
                return BodyTooLarge;
            }

            return SanitizeBody(Encoding.UTF8.GetString(buffer, 0, bytesRead), request.ContentType);
        }
        catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or DecoderFallbackException)
        {
            _logger.BodyCaptureFailed("request", ex);
            return CaptureError;
        }
        finally
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Captures a response body using the request-aborted token.
    /// </summary>
    public Task<string?> CaptureResponseBodyAsync(HttpResponse response, Stream originalBody)
    {
        ArgumentNullException.ThrowIfNull(response);
        return CaptureResponseBodyAsync(response, originalBody, response.HttpContext.RequestAborted);
    }

    /// <summary>
    /// Captures a response body while observing both caller cancellation and request abort.
    /// </summary>
    public async Task<string?> CaptureResponseBodyAsync(
        HttpResponse response,
        Stream originalBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(originalBody);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            response.HttpContext.RequestAborted);
        var effectiveToken = linkedCancellation.Token;
        effectiveToken.ThrowIfCancellationRequested();

        if (response.Body is not MemoryStream memoryStream)
        {
            return null;
        }

        string? capturedBody = null;
        try
        {
            if (ShouldCaptureResponse && IsContentTypeAllowed(response.ContentType))
            {
                capturedBody = memoryStream.Length > _settings.MaxBodySizeBytes
                    ? BodyTooLarge
                    : SanitizeBody(
                        Encoding.UTF8.GetString(memoryStream.ToArray()),
                        response.ContentType);
            }
        }
        catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or DecoderFallbackException)
        {
            _logger.BodyCaptureFailed("response", ex);
            capturedBody = CaptureError;
        }
        finally
        {
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBody, effectiveToken).ConfigureAwait(false);
            memoryStream.Position = 0;
        }

        return capturedBody;
    }

    public string? SanitizeBody(string? body, string? contentType)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        if (Encoding.UTF8.GetByteCount(body) > _settings.MaxBodySizeBytes)
        {
            return BodyTooLarge;
        }

        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return NonJson;
        }

        return SanitizeJson(body);
    }

    private string SanitizeJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return InvalidJson;
            }

            RedactJsonPaths(node, _settings.RedactedJsonPaths);
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return InvalidJson;
        }
    }

    private static void RedactJsonPaths(JsonNode node, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            RedactPath(node, path);
        }
    }

    private static void RedactPath(JsonNode? node, string jsonPath)
    {
        if (node is null)
        {
            return;
        }

        var parts = jsonPath.TrimStart('$', '.').Split('.');
        RedactPathRecursive(node, parts, 0);
    }

    private static void RedactPathRecursive(JsonNode? node, string[] parts, int index)
    {
        if (node is null || index >= parts.Length)
        {
            return;
        }

        switch (node)
        {
            case JsonObject obj:
            {
                var currentPart = parts[index];
                var key = obj.Select(pair => pair.Key)
                    .FirstOrDefault(candidate => candidate.Equals(currentPart, StringComparison.OrdinalIgnoreCase));

                if (key is not null)
                {
                    if (index == parts.Length - 1)
                    {
                        obj[key] = RedactedValue;
                    }
                    else
                    {
                        RedactPathRecursive(obj[key], parts, index + 1);
                    }
                }

                foreach (var child in obj.Select(pair => pair.Value))
                {
                    RedactPathRecursive(child, parts, 0);
                }

                break;
            }
            case JsonArray array:
                foreach (var item in array)
                {
                    RedactPathRecursive(item, parts, index);
                }

                break;
        }
    }

    public Dictionary<string, string> SanitizeHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            result[header.Key] = _settings.RedactedHeaders.Any(redacted =>
                redacted.Equals(header.Key, StringComparison.OrdinalIgnoreCase))
                    ? RedactedValue
                    : header.Value.ToString();
        }

        return result;
    }
}
