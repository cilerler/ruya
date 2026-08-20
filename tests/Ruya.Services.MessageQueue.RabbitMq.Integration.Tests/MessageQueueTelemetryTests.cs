using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.InMemory;
using Ruya.Services.MessageQueue.Telemetry;

namespace Ruya.Services.MessageQueue.Integration.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MessageQueueTelemetryTests
{
    private static readonly string[] _metricTagKeys =
    [
        "messaging.consumer.group.name",
        "messaging.destination.name",
        "messaging.system",
        "ruya.message_queue.outcome"
    ];

    [TestMethod]
    public async Task EnabledTelemetry_EmitsCorrelatedProducerAndConsumerAndOneSuccessfulDelivery()
    {
        var topic = CreateTopicName();
        const string consumerGroup = "telemetry-success-consumer";
        using var collector = new TelemetryCollector(listenForActivities: true);
        await using var serviceProvider = CreateServiceProvider(enableTelemetry: true);
        var queue = await CreateQueueAsync(serviceProvider);
        var receivedEnvelope = new TaskCompletionSource<MessageEnvelope<TestMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            context =>
            {
                receivedEnvelope.TrySetResult(context.Envelope);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { ConsumerGroup = consumerGroup });

        using var upstream = new Activity("test upstream");
        upstream.SetIdFormat(ActivityIdFormat.W3C);
        upstream.Start();
        var upstreamTraceId = upstream.TraceId;
        var upstreamSpanId = upstream.SpanId;

        await queue.PublishAsync(topic, new TestMessage { Id = 1, Content = "success" });
        var envelope = await receivedEnvelope.Task.WaitAsync(TimeSpan.FromSeconds(5));
        upstream.Stop();
        await subscription.DisposeAsync();

        var activities = collector.GetActivities(topic);
        Assert.AreEqual(2, activities.Length, "A publish and a delivery must create exactly two queue spans.");
        var producer = activities.Single(activity => activity.Kind == ActivityKind.Producer);
        var consumer = activities.Single(activity => activity.Kind == ActivityKind.Consumer);

        Assert.AreEqual(ActivityIdFormat.W3C, producer.IdFormat);
        Assert.AreEqual(upstreamTraceId, producer.TraceId);
        Assert.AreEqual(upstreamSpanId, producer.ParentSpanId);
        Assert.AreEqual(producer.TraceId, consumer.TraceId);
        Assert.AreEqual(producer.SpanId, consumer.ParentSpanId);

        Assert.IsTrue(TryGetHeader(envelope.Headers, "traceparent", out var traceParent));
        Assert.IsTrue(ActivityContext.TryParse(traceParent, null, out var propagatedContext));
        Assert.AreEqual(producer.TraceId, propagatedContext.TraceId);
        Assert.AreEqual(producer.SpanId, propagatedContext.SpanId);

        var counter = collector.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic);
        var duration = collector.GetMeasurements(MessageQueueTelemetry.DeliveryDurationInstrumentName, topic);
        Assert.AreEqual(1, counter.Length);
        Assert.AreEqual(1D, counter[0].Value);
        Assert.AreEqual(1, duration.Length);
        Assert.IsTrue(duration[0].Value >= 0D);
        Assert.AreEqual("success", counter[0].Tags["ruya.message_queue.outcome"]);
        AssertMetricTagsAreBounded(counter.Concat(duration), [topic], [consumerGroup]);
    }

    [TestMethod]
    public async Task DisabledTelemetry_EmitsNothingAndDoesNotInjectTraceHeaders()
    {
        var topic = CreateTopicName();
        const string consumerGroup = "telemetry-disabled-consumer";
        using var collector = new TelemetryCollector(listenForActivities: true);
        await using var serviceProvider = CreateServiceProvider(enableTelemetry: false);
        var queue = await CreateQueueAsync(serviceProvider);
        var receivedEnvelope = new TaskCompletionSource<MessageEnvelope<TestMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            context =>
            {
                receivedEnvelope.TrySetResult(context.Envelope);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { ConsumerGroup = consumerGroup });

        using var upstream = new Activity("test upstream");
        upstream.SetIdFormat(ActivityIdFormat.W3C);
        upstream.Start();
        await queue.PublishAsync(
            topic,
            new TestMessage { Id = 2, Content = "disabled" },
            new PublishOptions
            {
                Headers = new Dictionary<string, object> { ["custom-header"] = "preserved" }
            });
        var envelope = await receivedEnvelope.Task.WaitAsync(TimeSpan.FromSeconds(5));
        upstream.Stop();
        await subscription.DisposeAsync();

        Assert.AreEqual("preserved", envelope.Headers!["custom-header"]);
        Assert.IsFalse(TryGetHeader(envelope.Headers, "traceparent", out _));
        Assert.IsFalse(TryGetHeader(envelope.Headers, "tracestate", out _));
        Assert.AreEqual(0, collector.GetActivities(topic).Length);
        Assert.AreEqual(0, collector.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic).Length);
        Assert.AreEqual(0, collector.GetMeasurements(MessageQueueTelemetry.DeliveryDurationInstrumentName, topic).Length);
    }

    [TestMethod]
    public async Task EnabledTelemetry_WithoutActivityListener_DoesNotThrow()
    {
        var topic = CreateTopicName();
        await using var serviceProvider = CreateServiceProvider(enableTelemetry: true);
        var queue = await CreateQueueAsync(serviceProvider);
        var handled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            _ =>
            {
                handled.TrySetResult(true);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { ConsumerGroup = "no-listener-consumer" });

        await queue.PublishAsync(topic, new TestMessage { Id = 3, Content = "no listener" });
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await subscription.DisposeAsync();
    }

    [TestMethod]
    public async Task DeliveryOutcomes_EmitOneBoundedCounterAndDurationPerCompletedAttempt()
    {
        var prefix = CreateTopicName();
        var successTopic = $"{prefix}.success";
        var retryTopic = $"{prefix}.retry";
        var rejectTopic = $"{prefix}.reject";
        var unhandledTopic = $"{prefix}.unhandled";
        var topics = new[] { successTopic, retryTopic, rejectTopic, unhandledTopic };
        var consumerGroups = topics.ToDictionary(topic => topic, topic => $"{topic}.consumer", StringComparer.Ordinal);

        using var collector = new TelemetryCollector(listenForActivities: true);
        await using var serviceProvider = CreateServiceProvider(
            enableTelemetry: true,
            maxRetryAttempts: 2,
            retryDelay: TimeSpan.FromMilliseconds(1));
        var queue = await CreateQueueAsync(serviceProvider);

        var successHandled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var successSubscription = await queue.SubscribeAsync<TestMessage>(
            successTopic,
            _ =>
            {
                successHandled.TrySetResult(true);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { ConsumerGroup = consumerGroups[successTopic] });
        await queue.PublishAsync(
            successTopic,
            new TestMessage { Id = 10, Content = "success" },
            new PublishOptions { MessageId = "sensitive-message-id-success" });
        await successHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await successSubscription.DisposeAsync();

        var retryHandled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryAttempt = 0;
        await using var retrySubscription = await queue.SubscribeAsync<TestMessage>(
            retryTopic,
            _ =>
            {
                if (Interlocked.Increment(ref retryAttempt) == 1)
                {
                    return Task.FromResult(MessageResult.Retry("sensitive-retry-reason"));
                }

                retryHandled.TrySetResult(true);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { ConsumerGroup = consumerGroups[retryTopic] });
        await queue.PublishAsync(
            retryTopic,
            new TestMessage { Id = 11, Content = "retry" },
            new PublishOptions { MessageId = "sensitive-message-id-retry" });
        await retryHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await retrySubscription.DisposeAsync();

        var rejectHandled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rejectSubscription = await queue.SubscribeAsync<TestMessage>(
            rejectTopic,
            _ =>
            {
                rejectHandled.TrySetResult(true);
                return Task.FromResult(MessageResult.Reject("sensitive-reject-reason"));
            },
            new SubscribeOptions { ConsumerGroup = consumerGroups[rejectTopic] });
        await queue.PublishAsync(
            rejectTopic,
            new TestMessage { Id = 12, Content = "reject" },
            new PublishOptions { MessageId = "sensitive-message-id-reject" });
        await rejectHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForOutcomeAsync(collector, rejectTopic, "reject");
        await rejectSubscription.DisposeAsync();

        var unhandledHandled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unhandledAttempt = 0;
        await using var unhandledSubscription = await queue.SubscribeAsync<TestMessage>(
            unhandledTopic,
            _ =>
            {
                if (Interlocked.Increment(ref unhandledAttempt) == 1)
                {
                    return Task.FromException<MessageResult>(new InvalidOperationException("sensitive-unhandled-reason"));
                }

                unhandledHandled.TrySetResult(true);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions
            {
                ConsumerGroup = consumerGroups[unhandledTopic],
                RequeueOnException = true
            });
        await queue.PublishAsync(
            unhandledTopic,
            new TestMessage { Id = 13, Content = "unhandled" },
            new PublishOptions { MessageId = "sensitive-message-id-unhandled" });
        await unhandledHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await unhandledSubscription.DisposeAsync();

        AssertOutcomeOnce(collector, successTopic, "success");
        AssertOutcomeOnce(collector, retryTopic, "retry");
        AssertOutcomeOnce(collector, retryTopic, "success");
        AssertOutcomeOnce(collector, rejectTopic, "reject");
        AssertOutcomeOnce(collector, unhandledTopic, "unhandled");
        AssertOutcomeOnce(collector, unhandledTopic, "success");

        var completedAttempts = collector.Measurements
            .Where(measurement => topics.Contains(measurement.Destination, StringComparer.Ordinal))
            .ToArray();
        Assert.AreEqual(12, completedAttempts.Length, "Each of six attempts must emit one counter and one duration.");
        AssertMetricTagsAreBounded(completedAttempts, topics, consumerGroups.Values);
    }

    [TestMethod]
    public async Task HostCancellation_PropagatesToHandlerWithoutDeliveryOutcomeOrDuration()
    {
        var topic = CreateTopicName();
        const string consumerGroup = "telemetry-cancellation-consumer";
        using var collector = new TelemetryCollector(listenForActivities: true);
        await using var serviceProvider = CreateServiceProvider(enableTelemetry: true);
        var queue = await CreateQueueAsync(serviceProvider);
        var handlerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCancellation = new TaskCompletionSource<OperationCanceledException>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            async context =>
            {
                handlerStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                    return MessageResult.Success();
                }
                catch (OperationCanceledException exception) when (context.CancellationToken.IsCancellationRequested)
                {
                    handlerCancellation.TrySetResult(exception);
                    throw;
                }
            },
            new SubscribeOptions { ConsumerGroup = consumerGroup });

        await queue.PublishAsync(topic, new TestMessage { Id = 20, Content = "cancel" });
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await subscription.DisposeAsync();
        var cancellation = await handlerCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(cancellation.CancellationToken.IsCancellationRequested);
        Assert.AreEqual(0, collector.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic).Length);
        Assert.AreEqual(0, collector.GetMeasurements(MessageQueueTelemetry.DeliveryDurationInstrumentName, topic).Length);

        var consumer = collector.GetActivities(topic).Single(activity => activity.Kind == ActivityKind.Consumer);
        Assert.IsFalse(consumer.Tags.ContainsKey("ruya.message_queue.outcome"));
    }

    private static ServiceProvider CreateServiceProvider(
        bool enableTelemetry,
        int maxRetryAttempts = 1,
        TimeSpan? retryDelay = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddMessageQueue(options =>
            {
                options.EnableTelemetry = enableTelemetry;
                options.Providers["telemetry"] = new ProviderConfiguration
                {
                    Type = "InMemory",
                    Enabled = true
                };
            })
            .AddInMemoryProvider(options =>
            {
                options.EnableDeadLetterQueue = true;
                options.MaxRetryAttempts = maxRetryAttempts;
                options.RetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(1);
            });

        return services.BuildServiceProvider();
    }

    private static async Task<IMessageQueue> CreateQueueAsync(ServiceProvider serviceProvider)
    {
        var factory = serviceProvider.GetRequiredService<IMessageQueueFactory>();
        return await factory.CreateQueueAsync("telemetry");
    }

    private static string CreateTopicName() => $"telemetry-{Guid.NewGuid():N}";

    private static bool TryGetHeader(
        IReadOnlyDictionary<string, string>? headers,
        string name,
        out string? value)
    {
        var header = headers?.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, name, StringComparison.OrdinalIgnoreCase));
        if (header is { Key: not null })
        {
            value = header.Value.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static void AssertOutcomeOnce(TelemetryCollector collector, string topic, string outcome)
    {
        var counters = collector.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic)
            .Where(measurement => string.Equals(measurement.Outcome, outcome, StringComparison.Ordinal))
            .ToArray();
        var durations = collector.GetMeasurements(MessageQueueTelemetry.DeliveryDurationInstrumentName, topic)
            .Where(measurement => string.Equals(measurement.Outcome, outcome, StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(1, counters.Length, $"Expected one '{outcome}' delivery counter for '{topic}'.");
        Assert.AreEqual(1D, counters[0].Value);
        Assert.AreEqual(1, durations.Length, $"Expected one '{outcome}' delivery duration for '{topic}'.");
        Assert.IsTrue(durations[0].Value >= 0D);
    }

    private static async Task WaitForOutcomeAsync(
        TelemetryCollector collector,
        string topic,
        string outcome)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var hasCounter = collector.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic)
                .Any(measurement => string.Equals(measurement.Outcome, outcome, StringComparison.Ordinal));
            var hasDuration = collector.GetMeasurements(MessageQueueTelemetry.DeliveryDurationInstrumentName, topic)
                .Any(measurement => string.Equals(measurement.Outcome, outcome, StringComparison.Ordinal));
            if (hasCounter && hasDuration)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for '{outcome}' telemetry for '{topic}'.");
    }

    private static void AssertMetricTagsAreBounded(
        IEnumerable<MeasurementSnapshot> measurements,
        IEnumerable<string> expectedTopics,
        IEnumerable<string> expectedConsumerGroups)
    {
        var topics = expectedTopics.ToHashSet(StringComparer.Ordinal);
        var consumerGroups = expectedConsumerGroups.ToHashSet(StringComparer.Ordinal);
        var outcomes = new HashSet<string>(["success", "retry", "reject", "unhandled"], StringComparer.Ordinal);

        foreach (var tags in measurements.Select(measurement => measurement.Tags))
        {
            CollectionAssert.AreEquivalent(_metricTagKeys, tags.Keys.ToArray());
            Assert.AreEqual("in_memory", tags["messaging.system"]);
            Assert.IsTrue(topics.Contains((string)tags["messaging.destination.name"]!));
            Assert.IsTrue(consumerGroups.Contains((string)tags["messaging.consumer.group.name"]!));
            Assert.IsTrue(outcomes.Contains((string)tags["ruya.message_queue.outcome"]!));
            Assert.IsFalse(tags.Keys.Any(key =>
                key.Contains("id", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("type", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("reason", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("count", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(tags.Values
                .OfType<string>()
                .Any(value => value.Contains("sensitive-", StringComparison.Ordinal)));
        }
    }

    private sealed class TelemetryCollector : IDisposable
    {
        private readonly ConcurrentQueue<ActivitySnapshot> _activities = new();
        private readonly ConcurrentQueue<MeasurementSnapshot> _measurements = new();
        private readonly ActivityListener? _activityListener;
        private readonly MeterListener _meterListener;

        public TelemetryCollector(bool listenForActivities)
        {
            if (listenForActivities)
            {
                _activityListener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == MessageQueueTelemetry.InstrumentationName,
                    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                    SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStopped = activity => _activities.Enqueue(ActivitySnapshot.Create(activity))
                };
                ActivitySource.AddActivityListener(_activityListener);
            }

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == MessageQueueTelemetry.InstrumentationName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                _measurements.Enqueue(MeasurementSnapshot.Create(instrument, value, tags)));
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                _measurements.Enqueue(MeasurementSnapshot.Create(instrument, value, tags)));
            _meterListener.Start();
        }

        public MeasurementSnapshot[] Measurements => _measurements.ToArray();

        public ActivitySnapshot[] GetActivities(string topic)
        {
            return _activities
                .Where(activity => string.Equals(activity.Destination, topic, StringComparison.Ordinal))
                .ToArray();
        }

        public MeasurementSnapshot[] GetMeasurements(string instrumentName, string topic)
        {
            return _measurements
                .Where(measurement =>
                    string.Equals(measurement.InstrumentName, instrumentName, StringComparison.Ordinal) &&
                    string.Equals(measurement.Destination, topic, StringComparison.Ordinal))
                .ToArray();
        }

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener?.Dispose();
        }
    }

    private sealed record ActivitySnapshot(
        ActivityKind Kind,
        ActivityIdFormat IdFormat,
        ActivityTraceId TraceId,
        ActivitySpanId SpanId,
        ActivitySpanId ParentSpanId,
        IReadOnlyDictionary<string, object?> Tags)
    {
        public string? Destination => Tags.TryGetValue("messaging.destination.name", out var value) ? value as string : null;

        public static ActivitySnapshot Create(Activity activity)
        {
            return new ActivitySnapshot(
                activity.Kind,
                activity.IdFormat,
                activity.TraceId,
                activity.SpanId,
                activity.ParentSpanId,
                activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal));
        }
    }

    private sealed record MeasurementSnapshot(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, object?> Tags)
    {
        public string? Destination => Tags.TryGetValue("messaging.destination.name", out var value) ? value as string : null;

        public string? Outcome => Tags.TryGetValue("ruya.message_queue.outcome", out var value) ? value as string : null;

        public static MeasurementSnapshot Create<T>(
            Instrument instrument,
            T value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
        {
            var capturedTags = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                capturedTags[tag.Key] = tag.Value;
            }

            return new MeasurementSnapshot(
                instrument.Name,
                Convert.ToDouble(value, CultureInfo.InvariantCulture),
                capturedTags);
        }
    }
}
