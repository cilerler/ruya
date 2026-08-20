using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.Serialization;

/// <summary>
/// JSON-based message serializer using System.Text.Json
/// </summary>
public sealed class JsonMessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerContext[] _contexts;
    private readonly IMessageJsonTypeInfoResolver _messageTypeInfoResolver;
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Creates a new JSON message serializer with default options
    /// </summary>
    public JsonMessageSerializer() : this(CreateDefaultOptions(), Array.Empty<JsonSerializerContext>())
    {
    }

    /// <summary>
    /// Creates a new JSON message serializer with custom options
    /// </summary>
    public JsonMessageSerializer(JsonSerializerOptions options)
        : this(options, Array.Empty<JsonSerializerContext>())
    {
    }

    private JsonMessageSerializer(
        JsonSerializerOptions options,
        IEnumerable<JsonSerializerContext> contexts)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _contexts = contexts?
            .Where(static context => context is not null)
            .DistinctBy(static context => context.GetType())
            .ToArray()
            ?? throw new ArgumentNullException(nameof(contexts));
        _messageTypeInfoResolver = new MessageJsonTypeInfoResolver(_contexts);
    }

    internal JsonMessageSerializer(
        IEnumerable<JsonSerializerContext> contexts,
        IMessageJsonTypeInfoResolver messageTypeInfoResolver)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(messageTypeInfoResolver);

        _options = CreateDefaultOptions();
        _contexts = contexts
            .Where(static context => context is not null)
            .Distinct()
            .ToArray();
        _messageTypeInfoResolver = messageTypeInfoResolver;

        foreach (var context in _contexts)
        {
            _options.TypeInfoResolverChain.Add(context);
        }

        if (_contexts.Length > 0)
        {
            // Ruya's envelope and framework metadata use the reflection fallback. Registered producer
            // contexts precede it, so application payload metadata never reaches that fallback.
            _options.TypeInfoResolverChain.Add(
                new InfrastructureJsonTypeInfoResolver(_messageTypeInfoResolver));
        }
    }

    /// <inheritdoc />
    public byte[] Serialize<TMessage>(TMessage message) where TMessage : class
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        EnsureSourceGeneratedPayloadMetadata(typeof(TMessage));
        return JsonSerializer.SerializeToUtf8Bytes(message, _options);
    }

    /// <inheritdoc />
    public TMessage Deserialize<TMessage>(byte[] data) where TMessage : class
    {
        if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));
        EnsureSourceGeneratedPayloadMetadata(typeof(TMessage));
        return JsonSerializer.Deserialize<TMessage>(data, _options)
            ?? throw new InvalidOperationException("Deserialization resulted in null");
    }

    /// <inheritdoc />
    public object Deserialize(byte[] data, Type messageType)
    {
        if (data == null || data.Length == 0) throw new ArgumentNullException(nameof(data));
        if (messageType == null) throw new ArgumentNullException(nameof(messageType));

        EnsureSourceGeneratedPayloadMetadata(messageType);
        return JsonSerializer.Deserialize(data, messageType, _options)
            ?? throw new InvalidOperationException("Deserialization resulted in null");
    }

    /// <inheritdoc />
    public string ContentType => "application/json";

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
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

        return options;
    }

    private void EnsureSourceGeneratedPayloadMetadata(Type serializedType)
    {
        if (!_messageTypeInfoResolver.HasRegistrations ||
            !serializedType.IsGenericType ||
            serializedType.GetGenericTypeDefinition() != typeof(MessageEnvelope<>))
        {
            return;
        }

        var payloadType = serializedType.GenericTypeArguments[0];
        if (_messageTypeInfoResolver.GetTypeInfo(payloadType) is not null)
        {
            return;
        }

        throw new NotSupportedException(
            $"No registered source-generated JsonSerializerContext provides metadata for message payload type '{payloadType.FullName}'. " +
            "Register the producer-owned context with AddJsonSerializerContext().");
    }

    private sealed class InfrastructureJsonTypeInfoResolver(
        IMessageJsonTypeInfoResolver messageTypeInfoResolver) : IJsonTypeInfoResolver
    {
        private readonly DefaultJsonTypeInfoResolver _fallback = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (messageTypeInfoResolver.GetTypeInfo(type) is not null)
            {
                throw new InvalidOperationException(
                    $"Registered source-generated metadata for '{type.FullName}' was not selected by the JSON resolver chain. " +
                    "The application contract will not be serialized through reflection.");
            }

            return _fallback.GetTypeInfo(type, options);
        }
    }
}
