using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Ruya.Services.MessageQueue.Serialization;

internal sealed class MessageJsonTypeInfoResolver : IMessageJsonTypeInfoResolver
{
    private readonly JsonSerializerContext[] _contexts;

    public MessageJsonTypeInfoResolver(IEnumerable<JsonSerializerContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        _contexts = contexts
            .Where(static context => context is not null)
            .Distinct()
            .ToArray();
    }

    public bool HasRegistrations => _contexts.Length > 0;

    public JsonTypeInfo? GetTypeInfo(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        foreach (var context in _contexts)
        {
            var typeInfo = context.GetTypeInfo(messageType);
            if (typeInfo is not null)
            {
                return typeInfo;
            }
        }

        return null;
    }
}
