using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Sanitizes JSON content by redacting sensitive fields.
/// </summary>
internal static class JsonSanitizer
{
    private const string RedactedValue = "[REDACTED]";

    public static string Sanitize(string body, List<string> redactedPaths)
    {
        if (string.IsNullOrEmpty(body)) return body;

        try
        {
            var node = JsonNode.Parse(body);
            if (node is null) return body;

            foreach (var path in redactedPaths)
            {
                RedactPath(node, path);
            }

            return node.ToJsonString();
        }
        catch
        {
            return body;
        }
    }

    private static void RedactPath(JsonNode node, string path)
    {
        var parts = path.TrimStart('$', '.').Split('.');
        RedactPathRecursive(node, parts, 0);
    }

    private static void RedactPathRecursive(JsonNode? node, string[] parts, int index)
    {
        if (node is null || index >= parts.Length) return;

        if (node is JsonObject obj)
        {
            var key = obj.AsObject()
                .Select(kvp => kvp.Key)
                .FirstOrDefault(k => k.Equals(parts[index], StringComparison.OrdinalIgnoreCase));

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

            // Recurse into all children for nested redaction
            foreach (var child in obj.AsObject().Select(kvp => kvp.Value))
            {
                RedactPathRecursive(child, parts, 0);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                RedactPathRecursive(item, parts, index);
            }
        }
    }
}
