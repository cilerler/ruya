using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Abstractions.Models;

namespace Ruya.Services.TokenBroker.Unit.Tests;

internal static class TestSigningKeys
{
    private static readonly Lazy<(string PrivateKey, string PublicKey)> Keys = new(CreateKeys);
    private static readonly Lazy<(string PrivateKey, string PublicKey)> PreviousKeys = new(CreateKeys);

    public const string KeyId = "test-key-1";
    public const string PreviousKeyId = "test-key-previous";
    public static string PrivateKeyPem => Keys.Value.PrivateKey;
    public static string PublicKeyPem => Keys.Value.PublicKey;
    public static string PreviousPrivateKeyPem => PreviousKeys.Value.PrivateKey;
    public static string PreviousPublicKeyPem => PreviousKeys.Value.PublicKey;

    private static (string PrivateKey, string PublicKey) CreateKeys()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
}

internal sealed class PassThroughDistributedLock : IDistributedLock
{
    public async Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue = null,
        LockOptions? options = null)
    {
        await callback(CancellationToken.None);
        return LockResult.Succeeded();
    }
}

internal sealed class CapturingDistributedLock : IDistributedLock
{
    public LockOptions? LastOptions { get; private set; }

    public async Task<LockResult> AcquireAndExecuteWithLockAsync(
        Func<CancellationToken, Task> callback,
        string lockKey,
        string? lockValue = null,
        LockOptions? options = null)
    {
        LastOptions = options;
        await callback(CancellationToken.None);
        return LockResult.Succeeded();
    }
}
