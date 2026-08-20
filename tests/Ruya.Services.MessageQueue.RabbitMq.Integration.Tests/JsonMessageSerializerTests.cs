using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.Serialization;

namespace Ruya.Services.MessageQueue.Integration.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class JsonMessageSerializerTests
{
    [TestMethod]
    public void AddJsonSerializerContext_UsesProducerMetadataForEnvelopePayload()
    {
        var services = new ServiceCollection();
        services.AddMessageQueue(_ => { })
            .AddJsonSerializerContext(QueueContractJsonSerializerContext.Default);

        using var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var envelope = CreateEnvelope(new QueueSourceGeneratedPayload("generated"));

        var bytes = serializer.Serialize(envelope);
        var json = Encoding.UTF8.GetString(bytes);
        var roundTrip = serializer.Deserialize<MessageEnvelope<QueueSourceGeneratedPayload>>(bytes);

        StringAssert.Contains(json, "\"stableValue\":\"generated\"", StringComparison.Ordinal);
        Assert.AreEqual("generated", roundTrip.Payload.StableValue);
    }

    [TestMethod]
    public void AddJsonSerializerContext_RejectsPayloadMissingFromRegisteredContexts()
    {
        var services = new ServiceCollection();
        services.AddMessageQueue(_ => { })
            .AddJsonSerializerContext(QueueContractJsonSerializerContext.Default);
        using var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();
        var envelope = CreateEnvelope(new UnregisteredQueuePayload("reflection-only"));

        var exception = Assert.ThrowsExactly<NotSupportedException>(
            () => serializer.Serialize(envelope));

        StringAssert.Contains(
            exception.Message,
            typeof(UnregisteredQueuePayload).FullName!,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void ParameterlessSerializer_RetainsLegacyReflectionBehavior()
    {
        var serializer = new JsonMessageSerializer();
        var envelope = CreateEnvelope(new UnregisteredQueuePayload("legacy"));

        var bytes = serializer.Serialize(envelope);
        var roundTrip = serializer.Deserialize<MessageEnvelope<UnregisteredQueuePayload>>(bytes);

        Assert.AreEqual("legacy", roundTrip.Payload.Value);
    }

    [TestMethod]
    public void AddJsonSerializerContext_CombinesMultipleProducerContexts()
    {
        var services = new ServiceCollection();
        services.AddMessageQueue(_ => { })
            .AddJsonSerializerContext(QueueContractJsonSerializerContext.Default)
            .AddJsonSerializerContext(SecondQueueContractJsonSerializerContext.Default);
        using var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        var first = serializer.Deserialize<MessageEnvelope<QueueSourceGeneratedPayload>>(
            serializer.Serialize(CreateEnvelope(new QueueSourceGeneratedPayload("first"))));
        var second = serializer.Deserialize<MessageEnvelope<SecondQueueSourceGeneratedPayload>>(
            serializer.Serialize(CreateEnvelope(new SecondQueueSourceGeneratedPayload("second"))));

        Assert.AreEqual("first", first.Payload.StableValue);
        Assert.AreEqual("second", second.Payload.Value);
    }

    [TestMethod]
    public void AddJsonSerializerContext_UsesProducerContextWithDifferentGenerationOptions()
    {
        var services = new ServiceCollection();
        services.AddMessageQueue(_ => { })
            .AddJsonSerializerContext(QueueContractJsonSerializerContext.Default);
        using var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

        var bytes = serializer.Serialize(
            CreateEnvelope(new QueueSourceGeneratedPayload("producer-options")));
        var json = Encoding.UTF8.GetString(bytes);
        var roundTrip = serializer.Deserialize<MessageEnvelope<QueueSourceGeneratedPayload>>(bytes);

        StringAssert.Contains(json, "\"stableValue\":\"producer-options\"", StringComparison.Ordinal);
        Assert.AreEqual("producer-options", roundTrip.Payload.StableValue);
    }

    private static MessageEnvelope<TPayload> CreateEnvelope<TPayload>(TPayload payload)
        where TPayload : class => new()
        {
            MessageId = Guid.NewGuid().ToString("D"),
            MessageType = typeof(TPayload).FullName!,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload,
        };
}

public sealed record QueueSourceGeneratedPayload(string StableValue);

public sealed record UnregisteredQueuePayload(string Value);

public sealed record SecondQueueSourceGeneratedPayload(string Value);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(QueueSourceGeneratedPayload))]
public sealed partial class QueueContractJsonSerializerContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(SecondQueueSourceGeneratedPayload))]
public sealed partial class SecondQueueContractJsonSerializerContext : JsonSerializerContext
{
}
