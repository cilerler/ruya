using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Telemetry;
using Testcontainers.MsSql;

namespace Ruya.Services.MessageQueue.MsSql.Integration.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ServiceBrokerIntegrationTests
{
    private const string DatabaseName = "RuyaMessageQueueTests";
    private const string ProviderName = "mssql-contract";
    private const string ApplicationMessageType = "RuyaServicesMessageQueueMessage";
    private const string DeliveryCountHeader = "ruya.message_queue.delivery_count";
    private const string LegacyQueuePrefix = "RuyaServicesMessageQueueQueue_";
    private const string LegacyServicePrefix = "RuyaServicesMessageQueueService_";
    private const string HashedQueuePrefix = "RuyaServicesMessageQueueQueueV2_";
    private const string HashedServicePrefix = "RuyaServicesMessageQueueServiceV2_";
    private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(30);
    private static readonly int[] _expectedRetryDeliveryCounts = [1, 2, 3];

    private static MsSqlContainer? _container;
    private static string? _connectionString;

    [ClassInitialize]
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The database name is a test-owned constant and is quoted as a SQL identifier.")]
    public static async Task ClassInitialize(TestContext testContext)
    {
        ArgumentNullException.ThrowIfNull(testContext);
        _container = new MsSqlBuilder("cilerler/mssql-server-linux:2025-RTM-ubuntu-22.04")
            .WithPassword("YourStrong!Passw0rd")
            .Build();

        await _container.StartAsync();

        var masterConnectionString = _container.GetConnectionString();
        await using (var masterConnection = new SqlConnection(masterConnectionString))
        {
            await masterConnection.OpenAsync();
            await using var createDatabase = new SqlCommand(TestSqlResources.CreateDatabase, masterConnection);
            createDatabase.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = DatabaseName;
            createDatabase.Parameters.Add("@p1", SqlDbType.Bit).Value = false;
            await createDatabase.ExecuteNonQueryAsync();

            await using var brokerStatus = new SqlCommand(
                "SELECT is_broker_enabled FROM sys.databases WHERE name = @DatabaseName;",
                masterConnection);
            brokerStatus.Parameters.AddWithValue("@DatabaseName", DatabaseName);
            var isBrokerEnabled = (bool)(await brokerStatus.ExecuteScalarAsync())!;
            if (!isBrokerEnabled)
            {
                await using var enableBroker = new SqlCommand(TestSqlResources.EnableServiceBroker, masterConnection);
                enableBroker.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = DatabaseName;
                enableBroker.Parameters.Add("@p1", SqlDbType.Bit).Value = false;
                await enableBroker.ExecuteNonQueryAsync();
            }
        }

        _connectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;

        await using var serviceProvider = CreateServiceProvider();
        await CreateQueueAsync(serviceProvider);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task SchemaInitialization_DisablesPoisonHandlingWithoutReenablingOperatorDisabledQueue()
    {
        var topic = CreateTopicName("schema");

        await using (var initialProvider = CreateServiceProvider())
        {
            await CreateQueueAsync(initialProvider);
            await EnsureTopicServiceAsync(topic);

            var initialState = await GetQueueStateAsync(topic);
            Assert.IsTrue(initialState.IsReceiveEnabled);
            Assert.IsTrue(initialState.IsEnqueueEnabled);
            Assert.IsFalse(initialState.IsPoisonMessageHandlingEnabled);

            await DisableQueueAsync(GetQueueName(topic));
        }

        await using (var upgradedProvider = CreateServiceProvider())
        {
            await CreateQueueAsync(upgradedProvider);

            var repairedState = await GetQueueStateAsync(topic);
            Assert.IsFalse(repairedState.IsReceiveEnabled, "Schema repair must preserve an operator-disabled queue.");
            Assert.IsFalse(repairedState.IsEnqueueEnabled, "Schema repair must preserve an operator-disabled queue.");
            Assert.IsFalse(
                repairedState.IsPoisonMessageHandlingEnabled,
                "Ruya owns finite retry and must disable Service Broker's automatic five-rollback queue stop.");

            await EnsureTopicServiceAsync(topic);
            var idempotentState = await GetQueueStateAsync(topic);
            Assert.AreEqual(repairedState, idempotentState, "Topic initialization must not re-enable the queue.");
        }
    }

    [TestMethod]
    public async Task PublishAndConsume_PreservesCallerIdentityAndEnvelopeMetadata()
    {
        var topic = CreateTopicName("success");
        var messageId = $"order/created/{Guid.NewGuid():N}";
        var handled = new TaskCompletionSource<MessageContext<TestMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var serviceProvider = CreateServiceProvider();
        var queue = await CreateQueueAsync(serviceProvider);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            context =>
            {
                handled.TrySetResult(context);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { ConsumerGroup = "success-consumer" });

        var returnedId = await queue.PublishAsync(
            topic,
            new TestMessage { Sequence = 1, Content = "delivered" },
            new PublishOptions
            {
                MessageId = messageId,
                CorrelationId = "correlation-42",
                Headers = new Dictionary<string, object> { ["tenant"] = "north" },
            });

        var context = await handled.Task.WaitAsync(_testTimeout);
        await WaitForApplicationMessageCountAsync(topic, expectedCount: 0);

        Assert.AreEqual(messageId, returnedId);
        Assert.AreEqual(messageId, context.Envelope.MessageId);
        Assert.AreEqual("correlation-42", context.Envelope.CorrelationId);
        Assert.AreEqual("north", context.Envelope.Headers!["tenant"]);
        Assert.AreEqual("1", context.Envelope.Headers[DeliveryCountHeader]);
        Assert.AreEqual(1, context.DeliveryCount);
        Assert.AreEqual(0L, await CountDeadLettersAsync(topic));
    }

    [TestMethod]
    public async Task RetryAtDeliveryCap_MovesStableArbitraryIdentityToDeadLetter()
    {
        var topic = CreateTopicName("retry");
        var messageId = $"outbox/Rüya:tenant-42/{new string('x', 300)}";
        var attemptIds = new ConcurrentQueue<string>();
        var deliveryCounts = new ConcurrentQueue<int>();
        var thirdAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = 0;

        using var telemetry = new TelemetryCollector();
        await using var serviceProvider = CreateServiceProvider();
        var queue = await CreateQueueAsync(serviceProvider);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            context =>
            {
                attemptIds.Enqueue(context.Envelope.MessageId);
                deliveryCounts.Enqueue(context.DeliveryCount);
                if (Interlocked.Increment(ref attempt) == 3)
                {
                    thirdAttempt.TrySetResult(true);
                }

                return Task.FromResult(MessageResult.Retry("transient"));
            },
            CreateFiniteRetryOptions(maxDeliveryCount: 3));

        var returnedId = await queue.PublishAsync(
            topic,
            new TestMessage { Sequence = 2, Content = "retry" },
            new PublishOptions { MessageId = messageId });

        await thirdAttempt.Task.WaitAsync(_testTimeout);
        var deadLetter = await WaitForDeadLetterAsync(topic);
        await WaitForMeasurementCountAsync(telemetry, topic, expectedCount: 3);

        Assert.AreEqual(messageId, returnedId);
        CollectionAssert.AreEqual(new[] { messageId, messageId, messageId }, attemptIds.ToArray());
        CollectionAssert.AreEqual(_expectedRetryDeliveryCounts, deliveryCounts.ToArray());
        Assert.AreEqual(messageId, deadLetter.MessageId);
        Assert.AreEqual(3, deadLetter.DeliveryAttempts);
        Assert.AreEqual("transient", deadLetter.ErrorMessage);

        var persistedEnvelope = new JsonMessageSerializer()
            .Deserialize<MessageEnvelope<TestMessage>>(deadLetter.MessagePayload);
        Assert.AreEqual(messageId, persistedEnvelope.MessageId);
        Assert.AreEqual("3", persistedEnvelope.Headers![DeliveryCountHeader]);

        var outcomes = telemetry.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic)
            .Select(measurement => measurement.Outcome)
            .ToArray();
        Assert.AreEqual(2, outcomes.Count(outcome => outcome == "retry"));
        Assert.AreEqual(1, outcomes.Count(outcome => outcome == "reject"));
    }

    [TestMethod]
    public async Task HostCancellation_AfterMoreThanFiveRollbacks_RedeliversAndLeavesQueueEnabled()
    {
        var topic = CreateTopicName("cancel");
        var messageId = $"cancel/{Guid.NewGuid():N}";
        var observedIds = new ConcurrentQueue<string>();
        var observedDeliveryCounts = new ConcurrentQueue<int>();

        using var telemetry = new TelemetryCollector();
        await using var serviceProvider = CreateServiceProvider();
        var queue = await CreateQueueAsync(serviceProvider);
        await queue.PublishAsync(
            topic,
            new TestMessage { Sequence = 3, Content = "cancel repeatedly" },
            new PublishOptions { MessageId = messageId });

        for (var rollback = 0; rollback < 6; rollback++)
        {
            var handlerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handlerCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var subscription = await queue.SubscribeAsync<TestMessage>(
                topic,
                async context =>
                {
                    observedIds.Enqueue(context.Envelope.MessageId);
                    observedDeliveryCounts.Enqueue(context.DeliveryCount);
                    handlerStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                        return MessageResult.Success();
                    }
                    catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                    {
                        handlerCancelled.TrySetResult(true);
                        throw;
                    }
                });

            await handlerStarted.Task.WaitAsync(_testTimeout);
            await subscription.DisposeAsync();
            await handlerCancelled.Task.WaitAsync(_testTimeout);
        }

        Assert.AreEqual(
            0,
            telemetry.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic).Length,
            "Host-cancelled attempts must not emit a completed delivery outcome.");

        var stateAfterRollbacks = await GetQueueStateAsync(topic);
        Assert.IsTrue(stateAfterRollbacks.IsReceiveEnabled);
        Assert.IsTrue(stateAfterRollbacks.IsEnqueueEnabled);
        Assert.IsFalse(stateAfterRollbacks.IsPoisonMessageHandlingEnabled);

        var finalDelivery = new TaskCompletionSource<MessageContext<TestMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var finalSubscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            context =>
            {
                observedIds.Enqueue(context.Envelope.MessageId);
                observedDeliveryCounts.Enqueue(context.DeliveryCount);
                finalDelivery.TrySetResult(context);
                return Task.FromResult(MessageResult.Success());
            });

        var finalContext = await finalDelivery.Task.WaitAsync(_testTimeout);
        await WaitForApplicationMessageCountAsync(topic, expectedCount: 0);
        await WaitForMeasurementCountAsync(telemetry, topic, expectedCount: 1);

        Assert.AreEqual(messageId, finalContext.Envelope.MessageId);
        Assert.IsTrue(observedIds.All(id => id == messageId));
        Assert.IsTrue(observedDeliveryCounts.All(count => count == 1));
        Assert.AreEqual(7, observedIds.Count);
        Assert.AreEqual(
            "success",
            telemetry.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic).Single().Outcome);
    }

    [TestMethod]
    public async Task MalformedApplicationBody_IsDeadLetteredAsOneUnhandledDelivery()
    {
        var topic = CreateTopicName("malformed");
        var malformedBody = Encoding.UTF8.GetBytes("{ definitely-not-json");
        var handlerCalls = 0;

        using var telemetry = new TelemetryCollector();
        await using var serviceProvider = CreateServiceProvider();
        var queue = await CreateQueueAsync(serviceProvider);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            _ =>
            {
                Interlocked.Increment(ref handlerCalls);
                return Task.FromResult(MessageResult.Success());
            });

        await SendRawApplicationMessageAsync(topic, malformedBody);
        var deadLetter = await WaitForDeadLetterAsync(topic);
        await WaitForMeasurementCountAsync(telemetry, topic, expectedCount: 1);

        Assert.AreEqual(0, handlerCalls);
        Assert.AreEqual(1, deadLetter.DeliveryAttempts);
        Assert.AreEqual("JsonException", deadLetter.ErrorMessage);
        CollectionAssert.AreEqual(malformedBody, deadLetter.MessagePayload);
        Assert.IsTrue(Guid.TryParse(deadLetter.MessageId, out _));

        var counter = telemetry.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic).Single();
        var duration = telemetry.GetMeasurements(MessageQueueTelemetry.DeliveryDurationInstrumentName, topic).Single();
        Assert.AreEqual("unhandled", counter.Outcome);
        Assert.AreEqual("unhandled", duration.Outcome);
    }

    [TestMethod]
    public async Task LogicalTopicMapping_SeparatesLegacyCollisionAndBoundsLongNames()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var dottedTopic = $"collision.{suffix}";
        var underscoredTopic = $"collision_{suffix}";
        var longTopic = $"long.{suffix}.{new string('x', 130)}";
        var dottedDelivery = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var underscoredDelivery = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var serviceProvider = CreateServiceProvider();
        var queue = await CreateQueueAsync(serviceProvider);
        await using var dottedSubscription = await queue.SubscribeAsync<TestMessage>(
            dottedTopic,
            context =>
            {
                dottedDelivery.TrySetResult(context.Envelope.Payload.Content);
                return Task.FromResult(MessageResult.Success());
            });
        await using var underscoredSubscription = await queue.SubscribeAsync<TestMessage>(
            underscoredTopic,
            context =>
            {
                underscoredDelivery.TrySetResult(context.Envelope.Payload.Content);
                return Task.FromResult(MessageResult.Success());
            });

        await Task.WhenAll(
            queue.PublishAsync(dottedTopic, new TestMessage { Sequence = 30, Content = "dotted" }),
            queue.PublishAsync(underscoredTopic, new TestMessage { Sequence = 31, Content = "underscored" }));

        Assert.AreEqual("dotted", await dottedDelivery.Task.WaitAsync(_testTimeout));
        Assert.AreEqual("underscored", await underscoredDelivery.Task.WaitAsync(_testTimeout));
        await WaitForApplicationMessageCountAsync(dottedTopic, expectedCount: 0);
        await WaitForApplicationMessageCountAsync(underscoredTopic, expectedCount: 0);

        var dottedTopology = GetTopologyNames(dottedTopic);
        var underscoredTopology = GetTopologyNames(underscoredTopic);
        Assert.AreNotEqual(dottedTopology.Queue, underscoredTopology.Queue);
        Assert.AreNotEqual(dottedTopology.Service, underscoredTopology.Service);
        Assert.AreEqual(1L, await CountQueuesAsync(dottedTopology.Queue));
        Assert.AreEqual(1L, await CountQueuesAsync(underscoredTopology.Queue));

        await queue.PublishAsync(longTopic, new TestMessage { Sequence = 32, Content = "bounded" });
        var longTopology = GetTopologyNames(longTopic);
        Assert.IsTrue(longTopology.Queue.Length <= 128);
        Assert.IsTrue(longTopology.Service.Length <= 128);
        Assert.AreEqual(1L, await CountApplicationMessagesAsync(longTopic));

        var overlongTopic = new string('z', 256);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.PublishAsync(overlongTopic, new TestMessage { Sequence = 33, Content = "invalid" }));
    }

    [TestMethod]
    public async Task ConcurrentFirstUse_PublishersAndSubscribersCreateOneQualifiedTopology()
    {
        var topic = CreateTopicName("firstuse");
        const int PublisherCount = 8;
        const int SubscriberCount = 4;
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allDelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveryCount = 0;

        await using var serviceProvider = CreateServiceProvider();
        var queue = await CreateQueueAsync(serviceProvider);

        async Task<IMessageSubscription> SubscribeAfterGateAsync()
        {
            await startGate.Task;
            return await queue.SubscribeAsync<TestMessage>(
                topic,
                _ =>
                {
                    if (Interlocked.Increment(ref deliveryCount) == PublisherCount)
                    {
                        allDelivered.TrySetResult(true);
                    }

                    return Task.FromResult(MessageResult.Success());
                });
        }

        async Task<string> PublishAfterGateAsync(int sequence)
        {
            await startGate.Task;
            return await queue.PublishAsync(
                topic,
                new TestMessage { Sequence = sequence, Content = $"parallel-{sequence}" });
        }

        var subscriptionTasks = Enumerable.Range(0, SubscriberCount)
            .Select(_ => SubscribeAfterGateAsync())
            .ToArray();
        var publishTasks = Enumerable.Range(0, PublisherCount)
            .Select(PublishAfterGateAsync)
            .ToArray();
        startGate.TrySetResult(true);

        var allOperations = subscriptionTasks
            .Cast<Task>()
            .Concat(publishTasks)
            .ToArray();

        try
        {
            await Task.WhenAll(allOperations);
            var messageIds = await Task.WhenAll(publishTasks);
            Assert.AreEqual(PublisherCount, messageIds.Distinct(StringComparer.Ordinal).Count());
            await allDelivered.Task.WaitAsync(_testTimeout);
            await WaitForApplicationMessageCountAsync(topic, expectedCount: 0);

            var topology = GetTopologyNames(topic);
            Assert.AreEqual(1L, await CountQueuesAsync(topology.Queue));
            Assert.AreEqual(1L, await CountServicesAsync(topology.Service, topology.Queue));
            Assert.AreEqual("dbo", await GetQueueSchemaAsync(topology.Queue));
        }
        finally
        {
            var subscriptions = (await Task.WhenAll(
                    subscriptionTasks.Where(task => task.IsCompletedSuccessfully)))
                .Distinct()
                .ToArray();
            foreach (var subscription in subscriptions)
            {
                await subscription.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task DifferentDefaultSchema_ProvisionsAndOperatesOnlyTheDboQueue()
    {
        var topic = CreateTopicName("schemauser");
        var operatorConnectionString = await CreateOperatorConnectionStringAsync();
        var handled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var serviceProvider = CreateServiceProvider(connectionString: operatorConnectionString);
        var queue = await CreateQueueAsync(serviceProvider);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            _ =>
            {
                handled.TrySetResult(true);
                return Task.FromResult(MessageResult.Success());
            });

        await queue.PublishAsync(topic, new TestMessage { Sequence = 40, Content = "qualified" });
        await handled.Task.WaitAsync(_testTimeout);
        await WaitForApplicationMessageCountAsync(topic, expectedCount: 0);

        var topology = GetTopologyNames(topic);
        Assert.AreEqual("dbo", await GetQueueSchemaAsync(topology.Queue));
        Assert.AreEqual(1L, await CountQueuesAsync(topology.Queue));
        Assert.AreEqual(1L, await CountServicesAsync(topology.Service, topology.Queue));
        Assert.AreEqual(0L, await CountQueuesAsync(topology.Queue, "RuyaOperatorSchema"));
    }

    [TestMethod]
    [SuppressMessage(
        "Security",
        "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "The test holds the private transition semaphore only to deterministically reproduce the lifetime race.")]
    public async Task ResumeAsync_WhenLifetimeEndsWhileWaiting_DoesNotReactivateSubscription()
    {
        var topic = CreateTopicName("resume");
        using var lifetime = new CancellationTokenSource();

        await using var serviceProvider = CreateServiceProvider();
        var queue = await CreateQueueAsync(serviceProvider);
        var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            _ => Task.FromResult(MessageResult.Success()),
            cancellationToken: lifetime.Token);
        SemaphoreSlim? transitionLock = null;
        var lockHeld = false;

        try
        {
            await subscription.PauseAsync();
            var transitionLockField = subscription.GetType().GetField(
                "_pauseTransitionLock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(transitionLockField);
            transitionLock = (SemaphoreSlim)transitionLockField.GetValue(subscription)!;
            await transitionLock.WaitAsync();
            lockHeld = true;

            var resume = subscription.ResumeAsync();
            await Task.Delay(50);
            Assert.IsFalse(resume.IsCompleted, "ResumeAsync should be waiting at the transition boundary.");

            await lifetime.CancelAsync();
            transitionLock.Release();
            lockHeld = false;

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await resume);
            Assert.IsFalse(subscription.IsActive);
        }
        finally
        {
            if (lockHeld)
            {
                transitionLock!.Release();
            }

            await subscription.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task PublishBatch_CommitsDistinctIdentitiesWithSiblingProducerSpans()
    {
        var topic = CreateTopicName("batch");
        var receivedIds = new ConcurrentQueue<string>();
        var allHandled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RecordingPublishMiddleware();

        using var telemetry = new TelemetryCollector();
        await using var serviceProvider = CreateServiceProvider(middleware: middleware);
        var queue = await CreateQueueAsync(serviceProvider);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            topic,
            context =>
            {
                receivedIds.Enqueue(context.Envelope.MessageId);
                if (receivedIds.Count == 3)
                {
                    allHandled.TrySetResult(true);
                }

                return Task.FromResult(MessageResult.Success());
            });

        using var upstream = new Activity("batch upstream");
        upstream.SetIdFormat(ActivityIdFormat.W3C);
        upstream.Start();
        var upstreamTraceId = upstream.TraceId;
        var upstreamSpanId = upstream.SpanId;

        var messageIds = await queue.PublishBatchAsync(
            topic,
            new[]
            {
                new TestMessage { Sequence = 10, Content = "first" },
                new TestMessage { Sequence = 11, Content = "second" },
                new TestMessage { Sequence = 12, Content = "third" },
            });
        upstream.Stop();

        await allHandled.Task.WaitAsync(_testTimeout);
        await WaitForApplicationMessageCountAsync(topic, expectedCount: 0);
        await WaitForMeasurementCountAsync(telemetry, topic, expectedCount: 3);

        Assert.AreEqual(3, messageIds.Count);
        Assert.AreEqual(3, messageIds.Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEquivalent(messageIds.ToArray(), receivedIds.ToArray());
        CollectionAssert.AreEquivalent(messageIds.ToArray(), middleware.MessageIds.ToArray());
        Assert.IsTrue(middleware.Topics.All(observedTopic => observedTopic == topic));

        var activities = telemetry.GetActivities(topic);
        var producers = activities.Where(activity => activity.Kind == ActivityKind.Producer).ToArray();
        var consumers = activities.Where(activity => activity.Kind == ActivityKind.Consumer).ToArray();
        Assert.AreEqual(3, producers.Length);
        Assert.AreEqual(3, consumers.Length);
        Assert.IsTrue(producers.All(activity => activity.TraceId == upstreamTraceId));
        Assert.IsTrue(producers.All(activity => activity.ParentSpanId == upstreamSpanId));
        Assert.AreEqual(3, producers.Select(activity => activity.SpanId).Distinct().Count());
        Assert.IsTrue(consumers.All(activity => activity.TraceId == upstreamTraceId));
        Assert.IsTrue(consumers.All(activity => producers.Any(producer => producer.SpanId == activity.ParentSpanId)));
    }

    [TestMethod]
    public async Task PublishBatch_MiddlewareChangesDestination_RoutesMessagesToChangedTopic()
    {
        var sourceTopic = CreateTopicName("batch-source");
        var destinationTopic = CreateTopicName("batch-destination");
        var receivedIds = new ConcurrentQueue<string>();
        var allHandled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new ReroutingPublishMiddleware(destinationTopic);

        await using var serviceProvider = CreateServiceProvider(middleware: middleware);
        var queue = await CreateQueueAsync(serviceProvider);
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            destinationTopic,
            context =>
            {
                receivedIds.Enqueue(context.Envelope.MessageId);
                if (receivedIds.Count == 2)
                {
                    allHandled.TrySetResult(true);
                }

                return Task.FromResult(MessageResult.Success());
            });

        var messageIds = await queue.PublishBatchAsync(
            sourceTopic,
            new[]
            {
                new TestMessage { Sequence = 13, Content = "first rerouted" },
                new TestMessage { Sequence = 14, Content = "second rerouted" },
            });

        await allHandled.Task.WaitAsync(_testTimeout);
        await WaitForApplicationMessageCountAsync(destinationTopic, expectedCount: 0);

        CollectionAssert.AreEquivalent(messageIds.ToArray(), receivedIds.ToArray());
        Assert.AreEqual(0L, await CountApplicationMessagesAsync(sourceTopic));
    }

    [TestMethod]
    public async Task PublishBatch_WhenSerializationFails_RollsBackEveryMessage()
    {
        var topic = CreateTopicName("rollback");

        using var telemetry = new TelemetryCollector();
        await using var serviceProvider = CreateServiceProvider(useFailingBatchSerializer: true);
        var queue = await CreateQueueAsync(serviceProvider);
        await EnsureTopicServiceAsync(topic);

        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.PublishBatchAsync(
            topic,
            new[]
            {
                new TestMessage { Sequence = 20, Content = "would be rolled back" },
                new TestMessage { Sequence = 21, Content = "serializer fails", BreakSerialization = true },
                new TestMessage { Sequence = 22, Content = "never attempted" },
            }));

        Assert.AreEqual(0L, await CountApplicationMessagesAsync(topic));
        Assert.AreEqual(0L, await CountDeadLettersAsync(topic));

        var producerActivities = telemetry.GetActivities(topic)
            .Where(activity => activity.Kind == ActivityKind.Producer)
            .ToArray();
        Assert.AreEqual(2, producerActivities.Length, "Only the first two batch entries should start spans.");
        Assert.IsTrue(producerActivities.All(activity => activity.Status == ActivityStatusCode.Error));
        Assert.AreEqual(
            0,
            telemetry.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic).Length);
    }

    private static ServiceProvider CreateServiceProvider(
        bool useFailingBatchSerializer = false,
        IMessageMiddleware? middleware = null,
        string? connectionString = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddMessageQueue(options =>
        {
            options.EnableTelemetry = true;
            options.Providers[ProviderName] = new ProviderConfiguration
            {
                Type = "MsSql",
                Enabled = true,
            };
        });

        if (useFailingBatchSerializer)
        {
            builder.AddSerializer<FailingBatchSerializer>();
        }
        if (middleware is not null)
        {
            services.AddSingleton(middleware);
        }

        builder.AddMsSql(options =>
        {
            options.ConnectionString = connectionString ?? GetConnectionString();
            options.AutoCreateSchema = true;
            options.AutoEnableServiceBroker = false;
            options.ReceiveTimeoutMs = 100;
            options.PollingIntervalMs = 10;
            options.MaxDeliveryAttempts = 3;
            options.CommandTimeoutSeconds = 15;
        });

        return services.BuildServiceProvider();
    }

    private static async Task<IMessageQueue> CreateQueueAsync(ServiceProvider serviceProvider)
    {
        var factory = serviceProvider.GetRequiredService<IMessageQueueFactory>();
        return await factory.CreateQueueAsync(ProviderName);
    }

    private static SubscribeOptions CreateFiniteRetryOptions(int maxDeliveryCount)
    {
        return new SubscribeOptions
        {
            MaxDeliveryCount = maxDeliveryCount,
            RetryPolicy = new RetryPolicy
            {
                MaxRetryAttempts = maxDeliveryCount - 1,
                InitialDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(1),
                BackoffMultiplier = 1,
                UseExponentialBackoff = false,
                UseJitter = false,
            },
        };
    }

    private static async Task EnsureTopicServiceAsync(string topic)
    {
        var topology = GetTopologyNames(topic);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(TestSqlResources.CreateTopicService, connection);
        command.Parameters.Add("@p0", SqlDbType.NVarChar, 255).Value = topic;
        command.Parameters.Add("@p1", SqlDbType.NVarChar, 128).Value = topology.Queue;
        command.Parameters.Add("@p2", SqlDbType.NVarChar, 128).Value = topology.Service;
        command.Parameters.Add("@p3", SqlDbType.NVarChar, 128).Value = (object?)topology.LegacyQueue ?? DBNull.Value;
        command.Parameters.Add("@p4", SqlDbType.NVarChar, 128).Value = (object?)topology.LegacyService ?? DBNull.Value;
        command.Parameters.Add("@p5", SqlDbType.NVarChar, 128).Direction = ParameterDirection.Output;
        command.Parameters.Add("@p6", SqlDbType.NVarChar, 128).Direction = ParameterDirection.Output;
        command.Parameters.Add("@p7", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync();
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The unique test topic is converted to a quoted Service Broker identifier and literal.")]
    private static async Task SendRawApplicationMessageAsync(string topic, byte[] body)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(TestSqlResources.SendRawApplicationMessage, connection);
        command.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = GetServiceName(topic);
        command.Parameters.Add("@p1", SqlDbType.VarBinary, -1).Value = body;
        command.Parameters.Add("@p2", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        command.Parameters.Add("@p3", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<QueueState> GetQueueStateAsync(string topic)
    {
        const string sql = @"
            SELECT is_receive_enabled, is_enqueue_enabled, is_poison_message_handling_enabled
            FROM sys.service_queues AS queue
            INNER JOIN sys.schemas AS queue_schema ON queue_schema.schema_id = queue.schema_id
            WHERE queue.name = @QueueName
              AND queue_schema.name = N'dbo';";

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@QueueName", GetQueueName(topic));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync(), $"Expected a Service Broker queue for topic '{topic}'.");
        return new QueueState(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The unique test topic is converted to a quoted Service Broker queue identifier.")]
    private static async Task<long> CountApplicationMessagesAsync(string topic)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(TestSqlResources.CountApplicationMessages, connection) { CommandTimeout = 5 };
        command.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = GetQueueName(topic);
        command.Parameters.Add("@p1", SqlDbType.NVarChar, 256).Value = ApplicationMessageType;
        var messageCount = command.Parameters.Add("@p2", SqlDbType.BigInt);
        messageCount.Direction = ParameterDirection.Output;
        command.Parameters.Add("@p3", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync();
        return (long)messageCount.Value;
    }

    private static async Task<long> CountDeadLettersAsync(string topic)
    {
        const string sql = @"
            SELECT COUNT_BIG(*)
            FROM [dbo].[RuyaServicesMessageQueueDeadLetter]
            WHERE [TopicName] = @TopicName;";

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TopicName", topic);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountQueuesAsync(string queueName, string? schemaName = null)
    {
        const string sql = @"
            SELECT COUNT_BIG(*)
            FROM sys.service_queues AS queue
            INNER JOIN sys.schemas AS queue_schema ON queue_schema.schema_id = queue.schema_id
            WHERE queue.name = @QueueName
              AND (@SchemaName IS NULL OR queue_schema.name = @SchemaName);";

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 128).Value = queueName;
        command.Parameters.Add("@SchemaName", SqlDbType.NVarChar, 128).Value =
            schemaName is null ? DBNull.Value : schemaName;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountServicesAsync(string serviceName, string queueName)
    {
        const string sql = @"
            SELECT COUNT_BIG(*)
            FROM sys.services AS service
            INNER JOIN sys.service_queues AS queue ON queue.object_id = service.service_queue_id
            INNER JOIN sys.schemas AS queue_schema ON queue_schema.schema_id = queue.schema_id
            WHERE service.name = @ServiceName
              AND queue.name = @QueueName
              AND queue_schema.name = N'dbo';";

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ServiceName", SqlDbType.NVarChar, 128).Value = serviceName;
        command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 128).Value = queueName;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> GetQueueSchemaAsync(string queueName)
    {
        const string sql = @"
            SELECT queue_schema.name
            FROM sys.service_queues AS queue
            INNER JOIN sys.schemas AS queue_schema ON queue_schema.schema_id = queue.schema_id
            WHERE queue.name = @QueueName;";

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@QueueName", SqlDbType.NVarChar, 128).Value = queueName;
        return (string?)await command.ExecuteScalarAsync()
            ?? throw new AssertFailedException($"No Service Broker queue named '{queueName}' exists.");
    }

    private static async Task<string> CreateOperatorConnectionStringAsync()
    {
        const string loginName = "RuyaQueueOperator";
        var password = $"Ruya-{Convert.ToHexString(RandomNumberGenerator.GetBytes(18))}!aA1";
        await using (var connection = new SqlConnection(GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(TestSqlResources.CreateOperator, connection);
            command.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = loginName;
            command.Parameters.Add("@p1", SqlDbType.NVarChar, 128).Value = password;
            command.Parameters.Add("@p2", SqlDbType.Bit).Value = false;
            await command.ExecuteNonQueryAsync();
        }
        var connectionString = new SqlConnectionStringBuilder(GetConnectionString())
        {
            IntegratedSecurity = false,
            UserID = loginName,
            Password = password,
        };
        return connectionString.ConnectionString;
    }

    private static async Task WaitForApplicationMessageCountAsync(string topic, long expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow + _testTimeout;
        do
        {
            if (await CountApplicationMessagesAsync(topic) == expectedCount)
            {
                return;
            }

            await Task.Delay(25);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"Topic '{topic}' did not reach {expectedCount} application messages within {_testTimeout}.");
    }

    private static async Task<DeadLetterRow> WaitForDeadLetterAsync(string topic)
    {
        const string sql = @"
            SELECT TOP (1) [MessageId], [MessagePayload], [ErrorMessage], [DeliveryAttempts]
            FROM [dbo].[RuyaServicesMessageQueueDeadLetter]
            WHERE [TopicName] = @TopicName
            ORDER BY [Id] DESC;";

        var deadline = DateTimeOffset.UtcNow + _testTimeout;
        do
        {
            await using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TopicName", topic);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new DeadLetterRow(
                    reader.GetString(0),
                    (byte[])reader.GetValue(1),
                    await reader.IsDBNullAsync(2) ? null : reader.GetString(2),
                    reader.GetInt32(3));
            }

            await Task.Delay(25);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new AssertFailedException($"No dead-letter row appeared for topic '{topic}' within {_testTimeout}.");
    }

    private static async Task WaitForMeasurementCountAsync(
        TelemetryCollector telemetry,
        string topic,
        int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow + _testTimeout;
        do
        {
            if (telemetry.GetMeasurements(MessageQueueTelemetry.DeliveryAttemptsInstrumentName, topic).Length == expectedCount)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"Topic '{topic}' did not emit {expectedCount} completed-delivery measurements within {_testTimeout}.");
    }

    private static async Task DisableQueueAsync(string queueName)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(TestSqlResources.DisableQueue, connection);
        command.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = queueName;
        command.Parameters.Add("@p1", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync();
    }

    private static string GetConnectionString() =>
        _connectionString ?? throw new InvalidOperationException("The SQL Server test container is not initialized.");

    private static string CreateTopicName(string purpose) => $"mq.{purpose}.{Guid.NewGuid():N}";

    private static string GetServiceName(string topic) => GetTopologyNames(topic).Service;

    private static string GetQueueName(string topic) => GetTopologyNames(topic).Queue;

    private static PhysicalTopology GetTopologyNames(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (topic.Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(topic));
        }

        var suffix = topic.Replace(".", "_", StringComparison.Ordinal);
        var canUseLegacyName = !topic.Contains('_', StringComparison.Ordinal) &&
            topic.All(static character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-') &&
            LegacyQueuePrefix.Length + suffix.Length <= 128 &&
            LegacyServicePrefix.Length + suffix.Length <= 128;
        if (canUseLegacyName)
        {
            return new PhysicalTopology(
                LegacyQueuePrefix + suffix,
                LegacyServicePrefix + suffix,
                null,
                null);
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(topic)));
        var legacyQueue = LegacyQueuePrefix + suffix;
        var legacyService = LegacyServicePrefix + suffix;
        return new PhysicalTopology(
            HashedQueuePrefix + digest,
            HashedServicePrefix + digest,
            legacyQueue.Length <= 128 ? legacyQueue : null,
            legacyService.Length <= 128 ? legacyService : null);
    }

    private sealed record QueueState(
        bool IsReceiveEnabled,
        bool IsEnqueueEnabled,
        bool IsPoisonMessageHandlingEnabled);

    private sealed record DeadLetterRow(
        string MessageId,
        byte[] MessagePayload,
        string? ErrorMessage,
        int DeliveryAttempts);

    private sealed record PhysicalTopology(
        string Queue,
        string Service,
        string? LegacyQueue,
        string? LegacyService);

    private sealed class RecordingPublishMiddleware : MessageMiddleware
    {
        private readonly ConcurrentQueue<string> _messageIds = new();
        private readonly ConcurrentQueue<string> _topics = new();

        public IReadOnlyCollection<string> MessageIds => _messageIds;

        public IReadOnlyCollection<string> Topics => _topics;

        public override Task<string> PublishAsync<TMessage>(
            MessageEnvelope<TMessage> envelope,
            string topic,
            Func<MessageEnvelope<TMessage>, string, Task<string>> next,
            CancellationToken cancellationToken = default)
        {
            _messageIds.Enqueue(envelope.MessageId);
            _topics.Enqueue(topic);
            return next(envelope, topic);
        }
    }

    private sealed class ReroutingPublishMiddleware(string destinationTopic) : MessageMiddleware
    {
        public override Task<string> PublishAsync<TMessage>(
            MessageEnvelope<TMessage> envelope,
            string topic,
            Func<MessageEnvelope<TMessage>, string, Task<string>> next,
            CancellationToken cancellationToken = default)
        {
            return next(envelope, destinationTopic);
        }
    }

    private sealed class TelemetryCollector : IDisposable
    {
        private readonly ConcurrentQueue<ActivitySnapshot> _activities = new();
        private readonly ConcurrentQueue<MeasurementSnapshot> _measurements = new();
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;

        public TelemetryCollector()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == MessageQueueTelemetry.InstrumentationName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _activities.Enqueue(ActivitySnapshot.Create(activity)),
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == MessageQueueTelemetry.InstrumentationName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                _measurements.Enqueue(MeasurementSnapshot.Create(instrument, value, tags)));
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                _measurements.Enqueue(MeasurementSnapshot.Create(instrument, value, tags)));
            _meterListener.Start();
        }

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
            _activityListener.Dispose();
        }
    }

    private sealed record ActivitySnapshot(
        ActivityKind Kind,
        ActivityStatusCode Status,
        ActivityTraceId TraceId,
        ActivitySpanId SpanId,
        ActivitySpanId ParentSpanId,
        IReadOnlyDictionary<string, object?> Tags)
    {
        public string? Destination =>
            Tags.TryGetValue("messaging.destination.name", out var value) ? value as string : null;

        public static ActivitySnapshot Create(Activity activity)
        {
            return new ActivitySnapshot(
                activity.Kind,
                activity.Status,
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
        public string? Destination =>
            Tags.TryGetValue("messaging.destination.name", out var value) ? value as string : null;

        public string? Outcome =>
            Tags.TryGetValue("ruya.message_queue.outcome", out var value) ? value as string : null;

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

public sealed class TestMessage
{
    public int Sequence { get; set; }

    public string Content { get; set; } = string.Empty;

    public bool BreakSerialization { get; set; }
}

public sealed class FailingBatchSerializer : IMessageSerializer
{
    private readonly JsonMessageSerializer _inner = new();

    public string ContentType => _inner.ContentType;

    public byte[] Serialize<TMessage>(TMessage message) where TMessage : class
    {
        if (message is MessageEnvelope<TestMessage> { Payload.BreakSerialization: true })
        {
            throw new InvalidOperationException("Intentional batch serialization failure.");
        }

        return _inner.Serialize(message);
    }

    public TMessage Deserialize<TMessage>(byte[] data) where TMessage : class =>
        _inner.Deserialize<TMessage>(data);

    public object Deserialize(byte[] data, Type messageType) => _inner.Deserialize(data, messageType);
}
