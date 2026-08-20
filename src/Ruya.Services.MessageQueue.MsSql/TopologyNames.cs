using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// Maps logical topics to bounded, collision-resistant Service Broker object names.
/// </summary>
internal static class TopologyNames
{
    private const int MaximumTopicLength = 255;
    private const int MaximumIdentifierLength = 128;
    private const string LegacyQueuePrefix = "RuyaServicesMessageQueueQueue_";
    private const string LegacyServicePrefix = "RuyaServicesMessageQueueService_";
    private const string HashedQueuePrefix = "RuyaServicesMessageQueueQueueV2_";
    private const string HashedServicePrefix = "RuyaServicesMessageQueueServiceV2_";

    internal static Names ForTopic(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (topic.Length > MaximumTopicLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topic),
                topic.Length,
                $"SQL Server topics cannot exceed {MaximumTopicLength} characters.");
        }

        var legacySuffix = topic.Replace('.', '_');
        var legacyQueue = LegacyQueuePrefix + legacySuffix;
        var legacyService = LegacyServicePrefix + legacySuffix;
        if (CanUseLegacyName(topic, legacySuffix))
        {
            return new Names(
                topic,
                legacyQueue,
                legacyService,
                null,
                null);
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(topic)));
        return new Names(
            topic,
            HashedQueuePrefix + digest,
            HashedServicePrefix + digest,
            legacyQueue.Length <= MaximumIdentifierLength ? legacyQueue : null,
            legacyService.Length <= MaximumIdentifierLength ? legacyService : null);
    }

    private static bool CanUseLegacyName(string topic, string suffix)
    {
        return !topic.Contains('_', StringComparison.Ordinal) &&
            string.Equals(topic, topic.ToLowerInvariant(), StringComparison.Ordinal) &&
            topic.All(static character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-') &&
            LegacyQueuePrefix.Length + suffix.Length <= MaximumIdentifierLength &&
            LegacyServicePrefix.Length + suffix.Length <= MaximumIdentifierLength;
    }

    internal sealed record Names(
        string Topic,
        string Queue,
        string Service,
        string? LegacyQueue,
        string? LegacyService);
}
