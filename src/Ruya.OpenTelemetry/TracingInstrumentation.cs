using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

using OpenTelemetry.Trace;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Configures OpenTelemetry instrumentation for ASP.NET Core, HttpClient, and SQL.
/// </summary>
internal static class TracingInstrumentation
{
    public static void ConfigureAspNetCore(TracerProviderBuilder tracing, HttpInstrumentationSettings settings)
    {
        var urlExclusions = new UrlExclusionMatcher(settings.ExcludeUrlPatterns);

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
                return !urlExclusions.IsExcluded(httpContext.Request.Path.Value);
            };
        });
    }

    public static void ConfigureHttpClient(TracerProviderBuilder tracing, HttpInstrumentationSettings settings)
    {
        var urlExclusions = new UrlExclusionMatcher(settings.ExcludeUrlPatterns);

        tracing.AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;

            options.EnrichWithException = (activity, exception) =>
            {
                activity.SetTag("exception.type", exception.GetType().FullName);
                activity.SetTag("exception.message", exception.Message);
            };

            options.FilterHttpRequestMessage = request =>
            {
                return !urlExclusions.IsExcluded(request.RequestUri?.PathAndQuery);
            };
        });
    }

    public static void ConfigureSql(TracerProviderBuilder tracing, SqlInstrumentationSettings settings)
    {
        var sanitizer = new SqlStatementSanitizer(settings);

        tracing.AddSqlClientInstrumentation(options =>
        {
            options.RecordException = settings.RecordException;

            options.EnrichWithSqlCommand = (activity, command) =>
            {
                if (command is DbCommand dbCommand)
                {
                    EnrichSqlCommand(activity, dbCommand, settings, sanitizer);
                }
            };
        });
    }

    internal static void EnrichSqlCommand(
        Activity activity,
        DbCommand command,
        SqlInstrumentationSettings settings,
        SqlStatementSanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sanitizer);

        activity.SetTag("db.command_type", command.CommandType.ToString());

        if (settings.CaptureCommandText && command.CommandType == CommandType.Text)
        {
            var sanitizedStatement = sanitizer.Sanitize(command.CommandText);
            activity.SetTag("db.query.text", sanitizedStatement);

            // Preserve the released 8.x telemetry field during the semantic-convention migration.
            activity.SetTag("db.statement", sanitizedStatement);
        }

        if (command.Parameters.Count > 0)
        {
            activity.SetTag("db.parameters.count", command.Parameters.Count);
        }
    }

}
