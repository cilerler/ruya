using System;
using System.Collections.Concurrent;
using System.IO;

namespace Ruya.Services.MessageQueue.MsSql.Integration.Tests;

internal static class TestSqlResources
{
    private const string Prefix =
        $"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.MessageQueue)}.{nameof(Ruya.Services.MessageQueue.MsSql)}.{nameof(Ruya.Services.MessageQueue.MsSql.Integration)}.{nameof(Ruya.Services.MessageQueue.MsSql.Integration.Tests)}.Resources.SQL.";
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    internal static string CreateDatabase => Load(nameof(CreateDatabase) + ".sql");
    internal static string EnableServiceBroker => Load(nameof(EnableServiceBroker) + ".sql");
    internal static string DisableQueue => Load(nameof(DisableQueue) + ".sql");
    internal static string CreateTopicService => Load(nameof(CreateTopicService) + ".sql");
    internal static string SendRawApplicationMessage => Load(nameof(SendRawApplicationMessage) + ".sql");
    internal static string CountApplicationMessages => Load(nameof(CountApplicationMessages) + ".sql");
    internal static string CreateOperator => Load(nameof(CreateOperator) + ".sql");

    private static string Load(string name) => Cache.GetOrAdd(name, static resourceName =>
    {
        var assembly = typeof(TestSqlResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(Prefix + resourceName)
            ?? throw new InvalidOperationException($"Embedded test SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
