using System;
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
    internal const string RegexTimeoutMarker = "[SQL_REDACTED:REGEX_TIMEOUT]";
    private const int RegexTimeoutMilliseconds = 250;

    private readonly SqlInstrumentationSettings _settings;
    private readonly Regex[] _sensitivePatterns;

    [GeneratedRegex(@"'[^']*'", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex StringLiteralPattern();

    [GeneratedRegex(@"(?<![\w@])[-+]?(?:\d+(?:\.\d+)?|\.\d+)(?:[eE][-+]?\d+)?(?![\w])", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex NumericLiteralPattern();

    [GeneratedRegex(@"0x[0-9A-Fa-f]+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex HexLiteralPattern();

    public SqlStatementSanitizer(IOptions<OpenTelemetrySettings> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Value.Sql;
        _sensitivePatterns = CompileSensitivePatterns(_settings.SensitivePatterns);
    }

    internal SqlStatementSanitizer(SqlInstrumentationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _sensitivePatterns = CompileSensitivePatterns(settings.SensitivePatterns);
    }

    public string Sanitize(string? statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
            return string.Empty;

        var result = statement;

        if (!_settings.SanitizeStatements)
            return Truncate(result);

        try
        {
            // Apply configured patterns before generic literal removal so field-level rules
            // can replace the complete sensitive expression.
            foreach (var pattern in _sensitivePatterns)
            {
                result = pattern.Replace(result, "[REDACTED]");
            }

            result = StringLiteralPattern().Replace(result, "'?'");
            result = HexLiteralPattern().Replace(result, "0x?");
            result = NumericLiteralPattern().Replace(result, "?");
        }
        catch (RegexMatchTimeoutException)
        {
            // Never fall back to the raw statement when a configured expression is too expensive.
            return RegexTimeoutMarker;
        }

        return Truncate(result);

        string Truncate(string value) => value.Length > _settings.MaxStatementLength
            ? value[.._settings.MaxStatementLength] + "...[TRUNCATED]"
            : value;
    }

    private static Regex[] CompileSensitivePatterns(IEnumerable<string> patterns) => patterns
        .Select(pattern => new Regex(
            pattern,
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds)))
        .ToArray();

    /// <summary>
    /// Sanitizes stored procedure parameters.
    /// </summary>
    public string SanitizeParameters(IEnumerable<KeyValuePair<string, object?>> parameters)
    {
        var sanitized = parameters.Select(p =>
        {
            var isSensitive = _settings.SensitivePatterns
                .Any(pattern => p.Key.Contains(pattern.Replace(@"\s*=\s*'[^']*'", "", StringComparison.Ordinal),
                    StringComparison.OrdinalIgnoreCase));

            var value = isSensitive ? "[REDACTED]" : "?";
            return $"@{p.Key}={value}";
        });

        return string.Join(", ", sanitized);
    }
}
