using System;

namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// Internal wrapper for messages in the InMemory provider
/// </summary>
internal sealed record MessageWrapper(
    byte[] SerializedMessage,  // Changed from string to byte[] to match IMessageSerializer.Serialize() return type
    string MessageId,
    byte Priority,
    DateTimeOffset? ExpiresAt,
    string RoutingKey);  // Routing key for pattern matching
