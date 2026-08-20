using System;
using System.IO;

namespace Ruya.Services.DistributedLock.MsSql;

internal static class SqlResources
{
    private const string Prefix =
        $"{nameof(Ruya)}.{nameof(Ruya.Services)}.{nameof(Ruya.Services.DistributedLock)}.{nameof(Ruya.Services.DistributedLock.MsSql)}.Resources.SQL.";
    private static readonly Lazy<string> GetApplicationLockModeResource = new(
        () => Load(nameof(GetApplicationLockMode) + ".sql"));

    internal static string GetApplicationLockMode => GetApplicationLockModeResource.Value;

    private static string Load(string name)
    {
        var assembly = typeof(SqlResources).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(Prefix + name)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{name}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
