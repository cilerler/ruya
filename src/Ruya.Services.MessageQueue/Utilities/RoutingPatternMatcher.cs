using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Ruya.Services.MessageQueue.Utilities;

/// <summary>
/// Utility for matching routing keys against RabbitMQ-style routing patterns
/// Supports wildcards: * (single word), # (zero or more words)
/// </summary>
public static class RoutingPatternMatcher
{
    /// <summary>
    /// Checks if a routing key matches a routing pattern
    /// </summary>
    /// <param name="routingKey">The routing key to match (e.g., "orders.us.created")</param>
    /// <param name="pattern">The pattern to match against (e.g., "orders.*.created", "orders.#")</param>
    /// <returns>True if the routing key matches the pattern</returns>
    /// <exception cref="ArgumentException">Thrown when the pattern is invalid or potentially malicious</exception>
    /// <remarks>
    /// Pattern syntax:
    /// - * (star) matches exactly one word
    /// - # (hash) matches zero or more words
    /// - Words are separated by dots (.)
    ///
    /// Examples:
    /// - "orders.*.created" matches "orders.us.created" but not "orders.created"
    /// - "orders.#" matches "orders", "orders.created", "orders.us.created"
    /// - "*.urgent" matches "orders.urgent", "inventory.urgent"
    /// </remarks>
    public static bool Matches(string routingKey, string pattern)
    {
        if (string.IsNullOrEmpty(routingKey))
            return false;

        if (string.IsNullOrEmpty(pattern))
            return false;

        // Validate pattern to prevent ReDoS attacks
        ValidatePattern(pattern);

        // Exact match (no wildcards)
        if (!pattern.Contains('*') && !pattern.Contains('#'))
            return routingKey == pattern;

        // Convert RabbitMQ pattern to regex
        var regexPattern = ConvertPatternToRegex(pattern);

        return Regex.IsMatch(routingKey, regexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Checks if a routing key matches any of the provided patterns
    /// </summary>
    /// <param name="routingKey">The routing key to match</param>
    /// <param name="patterns">The patterns to match against</param>
    /// <returns>True if the routing key matches at least one pattern</returns>
    public static bool MatchesAny(string routingKey, IEnumerable<string> patterns)
    {
        if (string.IsNullOrEmpty(routingKey))
            return false;

        if (patterns == null)
            return false;

        return patterns.Any(pattern => Matches(routingKey, pattern));
    }

    /// <summary>
    /// Validates a routing pattern to prevent ReDoS attacks
    /// </summary>
    /// <param name="pattern">The pattern to validate</param>
    /// <exception cref="ArgumentException">Thrown when the pattern is invalid</exception>
    private static void ValidatePattern(string pattern)
    {
        // Maximum pattern length to prevent excessive regex compilation time
        const int MaxPatternLength = 256;
        if (pattern.Length > MaxPatternLength)
        {
            throw new ArgumentException(
                $"Pattern length exceeds maximum allowed length of {MaxPatternLength} characters",
                nameof(pattern));
        }

        // Maximum number of wildcards to prevent exponential backtracking
        const int MaxWildcards = 20;
        var wildcardCount = pattern.Count(c => c == '*' || c == '#');
        if (wildcardCount > MaxWildcards)
        {
            throw new ArgumentException(
                $"Pattern contains too many wildcards (max: {MaxWildcards}, found: {wildcardCount})",
                nameof(pattern));
        }

        // Validate characters - only allow alphanumeric, dots, wildcards, hyphens, underscores
        if (pattern.Any(c => !char.IsLetterOrDigit(c) && c != '.' && c != '*' && c != '#' && c != '-' && c != '_'))
        {
            throw new ArgumentException(
                "Pattern contains invalid characters. Only alphanumeric, dots, wildcards (*, #), hyphens, and underscores are allowed",
                nameof(pattern));
        }
    }

    /// <summary>
    /// Converts a RabbitMQ routing pattern to a regular expression
    /// </summary>
    /// <param name="pattern">The RabbitMQ pattern (e.g., "orders.*.created")</param>
    /// <returns>A regex pattern string</returns>
    private static string ConvertPatternToRegex(string pattern)
    {
        // Escape special regex characters
        // Note: Regex.Escape escapes "." to "\." and "*" to "\*", but "#" remains "#" (not a regex special char)
        var escaped = Regex.Escape(pattern);

        // Replace RabbitMQ wildcards with regex patterns
        // Process in order from most specific to least specific to avoid conflicts

        // 1. Handle ".#" pattern at end (match zero or more additional words)
        //    "orders.#" → "orders" or "orders.anything"
        //    After escape: "orders\.\#" → Replace "\.\#" with "(?:\..*)?"
        escaped = escaped.Replace(@"\.\#", @"(?:\..*)?");

        // 2. Handle "#." pattern at start (match zero or more words before)
        //    "#.created" → "created" or "anything.created"
        //    After escape: "\#\.created" → Replace "\#\." with "(?:.*\.)?"
        escaped = escaped.Replace(@"\#\.", @"(?:.*\.)?");

        // 3. Handle standalone "#" (match everything)
        //    "#" → matches any routing key
        //    After escape: "\#" → Replace "\#" with ".*"
        escaped = escaped.Replace(@"\#", @".*");

        // 4. Handle "*" wildcard (match exactly one word)
        //    "orders.*.created" → "orders.us.created" but not "orders.created"
        //    After escape: "orders\.\*\.created" → Replace "\*" with "[^.]+"
        escaped = escaped.Replace(@"\*", @"[^.]+");

        // Anchor to start and end for exact matching
        return $"^{escaped}$";
    }

    /// <summary>
    /// Converts a RabbitMQ routing pattern to a Redis pattern
    /// </summary>
    /// <param name="pattern">The RabbitMQ pattern (e.g., "orders.*.created")</param>
    /// <returns>A Redis pattern (e.g., "orders:*:created")</returns>
    /// <remarks>
    /// Redis pattern syntax:
    /// - * matches any characters (like RabbitMQ #)
    /// - ? matches a single character
    /// - [abc] matches a, b, or c
    ///
    /// Conversion:
    /// - RabbitMQ "." → Redis ":"
    /// - RabbitMQ "*" → Redis "*" (but means one word in RabbitMQ)
    /// - RabbitMQ "#" → Redis "*"
    /// </remarks>
    public static string ConvertToRedisPattern(string rabbitMqPattern)
    {
        if (string.IsNullOrEmpty(rabbitMqPattern))
            return "*";

        // Replace . with : (Redis uses : as separator)
        var redisPattern = rabbitMqPattern.Replace('.', ':');

        // Replace # with * (both mean "zero or more")
        redisPattern = redisPattern.Replace('#', '*');

        // Note: * in RabbitMQ means "one word" but in Redis means "any characters"
        // This is a semantic difference, but Redis pattern matching is less precise
        // For Redis, we just use * for both cases

        return redisPattern;
    }
}
