using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Sanitizes SQL statements by removing sensitive data.
/// Thread-safe with compiled regex caching.
/// </summary>
public sealed partial class SqlStatementSanitizer
{
    private readonly SqlInstrumentationSettings _settings;
    private readonly ConcurrentDictionary<string, Regex> _sensitivePatternCache = new();

    // Pre-compiled regex for parameter sanitization
    [GeneratedRegex(@"'[^']*'", RegexOptions.Compiled)]
    private static partial Regex StringLiteralPattern();

    [GeneratedRegex(@"(?<==\s*)\d+(?=\s*[,\)]|$)", RegexOptions.Compiled)]
    private static partial Regex NumericLiteralPattern();

    [GeneratedRegex(@"0x[0-9A-Fa-f]+", RegexOptions.Compiled)]
    private static partial Regex HexLiteralPattern();

    public SqlStatementSanitizer(IOptions<OpenTelemetrySettings> options)
    {
        _settings = options.Value.Sql;

        // Pre-compile sensitive patterns
        foreach (var pattern in _settings.SensitivePatterns)
        {
            try
            {
                _sensitivePatternCache.TryAdd(pattern,
                    new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }
            catch (RegexParseException)
            {
                // Skip invalid patterns
            }
        }
    }

    public string Sanitize(string? statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
            return string.Empty;

        var result = statement;

        // Truncate if too long
        if (result.Length > _settings.MaxStatementLength)
        {
            result = result[.._settings.MaxStatementLength] + "...[TRUNCATED]";
        }

        if (!_settings.SanitizeStatements)
            return result;

        // Redact sensitive patterns first (password='xxx' -> password='[REDACTED]')
        foreach (var pattern in _sensitivePatternCache.Values)
        {
            result = pattern.Replace(result, "[REDACTED]");
        }

        // Replace string literals with placeholder
        result = StringLiteralPattern().Replace(result, "'?'");

        // Replace numeric literals in value positions
        result = NumericLiteralPattern().Replace(result, "?");

        // Replace hex literals
        result = HexLiteralPattern().Replace(result, "0x?");

        return result;
    }

    /// <summary>
    /// Sanitizes stored procedure parameters.
    /// </summary>
    public string SanitizeParameters(IEnumerable<KeyValuePair<string, object?>> parameters)
    {
        var sanitized = parameters.Select(p =>
        {
            var isSensitive = _settings.SensitivePatterns
                .Any(pattern => p.Key.Contains(pattern.Replace(@"\s*=\s*'[^']*'", ""),
                    StringComparison.OrdinalIgnoreCase));

            var value = isSensitive ? "[REDACTED]" : "?";
            return $"@{p.Key}={value}";
        });

        return string.Join(", ", sanitized);
    }
}
