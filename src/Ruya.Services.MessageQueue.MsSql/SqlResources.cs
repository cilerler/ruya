using System;
using System.Collections.Concurrent;
using System.IO;

namespace Ruya.Services.MessageQueue.MsSql;

internal static class SqlResources
{
    private const string Prefix =
        $"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.MessageQueue)}.{nameof(Ruya.Services.MessageQueue.MsSql)}.Resources.SQL.";
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    internal static string ServiceBrokerSchema => Load(nameof(ServiceBrokerSchema) + ".sql");
    internal static string CheckServiceBrokerEnabled => Load(nameof(CheckServiceBrokerEnabled) + ".sql");
    internal static string EnableServiceBroker => Load(nameof(EnableServiceBroker) + ".sql");
    internal static string CreateTopicService => Load(nameof(CreateTopicService) + ".sql");
    internal static string SendMessage => Load(nameof(SendMessage) + ".sql");
    internal static string ReceiveMessage => Load(nameof(ReceiveMessage) + ".sql");
    internal static string EndConversation => Load(nameof(EndConversation) + ".sql");
    internal static string InsertDeadLetter => Load(nameof(InsertDeadLetter) + ".sql");

    private static string Load(string name) => Cache.GetOrAdd(name, static resourceName =>
    {
        var assembly = typeof(SqlResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(Prefix + resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
