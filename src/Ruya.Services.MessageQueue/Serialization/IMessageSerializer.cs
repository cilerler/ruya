using System;

namespace Ruya.Services.MessageQueue.Serialization;

/// <summary>
/// Provides message serialization and deserialization
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Serializes a message to bytes
    /// </summary>
    byte[] Serialize<TMessage>(TMessage message) where TMessage : class;

    /// <summary>
    /// Deserializes bytes to a message
    /// </summary>
    TMessage Deserialize<TMessage>(byte[] data) where TMessage : class;

    /// <summary>
    /// Deserializes bytes to a message with type information
    /// </summary>
    object Deserialize(byte[] data, Type messageType);

    /// <summary>
    /// The content type for this serializer (e.g., "application/json")
    /// </summary>
    string ContentType { get; }
}
