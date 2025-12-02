using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using OpenTelemetry.Trace;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Configures OpenTelemetry instrumentation for ASP.NET Core, HttpClient, and SQL.
/// </summary>
internal static class TracingInstrumentation
{
    public static void ConfigureAspNetCore(TracerProviderBuilder tracing, HttpInstrumentationSettings settings)
    {
        tracing.AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;

            options.EnrichWithException = (activity, exception) =>
            {
                activity.SetTag("exception.type", exception.GetType().FullName);
                activity.SetTag("exception.message", exception.Message);
                activity.SetTag("exception.stacktrace", exception.StackTrace);
            };

            options.EnrichWithHttpRequest = (activity, request) =>
            {
                activity.SetTag("http.request.header.host", request.Host.Value);
                activity.SetTag("http.request.header.user_agent", request.Headers.UserAgent.ToString());

                if (settings.CaptureRequestBody && ShouldCaptureBody(request.Path.Value, request.ContentType, settings))
                {
                    CaptureRequestBodyAsync(activity, request, settings).GetAwaiter().GetResult();
                }
            };

            options.EnrichWithHttpResponse = (activity, response) =>
            {
                activity.SetTag("http.response.header.content_type", response.ContentType);

                if (response.ContentLength.HasValue)
                {
                    activity.SetTag("http.response.body.size", response.ContentLength.Value);
                }
            };

            options.Filter = httpContext =>
            {
                var path = httpContext.Request.Path.Value;
                if (string.IsNullOrEmpty(path)) return true;

                return !settings.ExcludeUrlPatterns.Any(pattern =>
                    path.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            };
        });
    }

    public static void ConfigureHttpClient(TracerProviderBuilder tracing, HttpInstrumentationSettings settings)
    {
        tracing.AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;

            options.EnrichWithException = (activity, exception) =>
            {
                activity.SetTag("exception.type", exception.GetType().FullName);
                activity.SetTag("exception.message", exception.Message);
            };

            options.EnrichWithHttpRequestMessage = (activity, request) =>
            {
                if (request.Content is null || !settings.CaptureRequestBody) return;

                var contentType = request.Content.Headers.ContentType?.MediaType;
                if (!IsAllowedContentType(contentType, settings.AllowedContentTypes)) return;

                try
                {
                    var contentBytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    var body = Encoding.UTF8.GetString(contentBytes);

                    if (body.Length > settings.MaxBodySizeBytes)
                    {
                        activity.SetTag("http.request.body", body[..settings.MaxBodySizeBytes] + "...[TRUNCATED]");
                    }
                    else
                    {
                        activity.SetTag("http.request.body", JsonSanitizer.Sanitize(body, settings.RedactedJsonPaths));
                    }
                }
                catch
                {
                    activity.SetTag("http.request.body", "[CAPTURE_ERROR]");
                }
            };

            options.EnrichWithHttpResponseMessage = (activity, response) =>
            {
                if (response.Content is null || !settings.CaptureResponseBody) return;

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (!IsAllowedContentType(contentType, settings.AllowedContentTypes)) return;

                try
                {
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (body.Length > settings.MaxBodySizeBytes)
                    {
                        activity.SetTag("http.response.body", body[..settings.MaxBodySizeBytes] + "...[TRUNCATED]");
                    }
                    else
                    {
                        activity.SetTag("http.response.body", JsonSanitizer.Sanitize(body, settings.RedactedJsonPaths));
                    }
                }
                catch
                {
                    activity.SetTag("http.response.body", "[CAPTURE_ERROR]");
                }
            };

            options.FilterHttpRequestMessage = request =>
            {
                var path = request.RequestUri?.PathAndQuery;
                if (string.IsNullOrEmpty(path)) return true;

                return !settings.ExcludeUrlPatterns.Any(pattern =>
                    path.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            };
        });
    }

    public static void ConfigureSql(TracerProviderBuilder tracing, SqlInstrumentationSettings settings)
    {
        tracing.AddSqlClientInstrumentation(options =>
        {
            options.RecordException = true;

            options.EnrichWithSqlCommand = (activity, command) =>
            {
                if (command is not DbCommand cmd) return;

                activity.SetTag("db.command_type", cmd.CommandType.ToString());

                if (settings.CaptureCommandText && cmd.CommandType == CommandType.Text)
                {
                    var sanitized = SanitizeSqlStatement(
                        cmd.CommandText,
                        settings.SanitizeStatements,
                        settings.MaxStatementLength,
                        settings.SensitivePatterns);

                    activity.SetTag("db.statement", sanitized);
                }

                if (cmd.Parameters.Count > 0)
                {
                    activity.SetTag("db.parameters.count", cmd.Parameters.Count);
                }
            };
        });
    }

    #region Helpers

    private static bool ShouldCaptureBody(string? path, string? contentType, HttpInstrumentationSettings settings)
    {
        if (string.IsNullOrEmpty(path)) return false;

        if (settings.ExcludeUrlPatterns.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        return IsAllowedContentType(contentType, settings.AllowedContentTypes);
    }

    private static bool IsAllowedContentType(string? contentType, List<string> allowedTypes)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        return allowedTypes.Any(t => contentType.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CaptureRequestBodyAsync(Activity activity, HttpRequest request, HttpInstrumentationSettings settings)
    {
        try
        {
            request.EnableBuffering();

            if (request.ContentLength > settings.MaxBodySizeBytes)
            {
                activity.SetTag("http.request.body", "[BODY_TOO_LARGE]");
                return;
            }

            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            activity.SetTag("http.request.body", JsonSanitizer.Sanitize(body, settings.RedactedJsonPaths));
        }
        catch
        {
            activity.SetTag("http.request.body", "[CAPTURE_ERROR]");
        }
    }

    private static string SanitizeSqlStatement(string statement, bool sanitize, int maxLength, List<string> sensitivePatterns)
    {
        if (string.IsNullOrEmpty(statement)) return string.Empty;

        var result = statement;

        if (result.Length > maxLength)
        {
            result = result[..maxLength] + "...[TRUNCATED]";
        }

        if (!sanitize) return result;

        foreach (var pattern in sensitivePatterns)
        {
            try
            {
                result = Regex.Replace(result, pattern, "[REDACTED]", RegexOptions.IgnoreCase);
            }
            catch
            {
                // Skip invalid patterns
            }
        }

        result = Regex.Replace(result, @"'[^']*'", "'?'");

        return result;
    }

    #endregion
}
