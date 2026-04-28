using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ruya.Services.MessageQueue.Serialization;

/// <summary>
/// JSON-based message serializer using System.Text.Json
/// </summary>
public sealed class JsonMessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Creates a new JSON message serializer with default options
    /// </summary>
    public JsonMessageSerializer() : this(CreateDefaultOptions())
    {
    }

    /// <summary>
    /// Creates a new JSON message serializer with custom options
    /// </summary>
    public JsonMessageSerializer(JsonSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public byte[] Serialize<TMessage>(TMessage message) where TMessage : class
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        return JsonSerializer.SerializeToUtf8Bytes(message, _options);
    }

    /// <inheritdoc />
    public TMessage Deserialize<TMessage>(byte[] data) where TMessage : class
    {
        if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));
        return JsonSerializer.Deserialize<TMessage>(data, _options)
            ?? throw new InvalidOperationException("Deserialization resulted in null");
    }

    /// <inheritdoc />
    public object Deserialize(byte[] data, Type messageType)
    {
        if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));
        if (messageType == null) throw new ArgumentNullException(nameof(messageType));

        return JsonSerializer.Deserialize(data, messageType, _options)
            ?? throw new InvalidOperationException("Deserialization resulted in null");
    }

    /// <inheritdoc />
    public string ContentType => "application/json";

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            // Forgiving on input. Producers (Ruya itself) emit camelCase via PropertyNamingPolicy above,
            // but external systems publishing onto our broker (e.g. Gateway-forwarded webhooks where the
            // supplier might use PascalCase) deserialize cleanly regardless of the casing they used.
            // Output is always camelCase regardless of this setting.
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }
}
