using System;
using System.Text.Json.Serialization.Metadata;

namespace Ruya.Services.MessageQueue.Serialization;

/// <summary>
/// Resolves producer-owned source-generated JSON metadata for application message contracts.
/// </summary>
public interface IMessageJsonTypeInfoResolver
{
    /// <summary>
    /// Gets whether the application registered any source-generated message contexts.
    /// </summary>
    bool HasRegistrations { get; }

    /// <summary>
    /// Resolves source-generated metadata for <paramref name="messageType"/>, or
    /// <see langword="null"/> when no registered context owns that contract.
    /// </summary>
    JsonTypeInfo? GetTypeInfo(Type messageType);
}
