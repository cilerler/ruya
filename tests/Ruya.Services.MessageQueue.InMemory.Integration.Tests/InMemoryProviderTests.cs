using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.InMemory;


using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;

namespace Ruya.Services.MessageQueue.InMemory.Integration.Tests;

[TestClass]
public class InMemoryProviderTests
{
    private IServiceProvider _serviceProvider = null!;
    private IMessageQueueFactory _factory = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddMessageQueue(options =>
        {
            options.Providers["inmemory"] = new ProviderConfiguration
            {
                Type = "InMemory",
                Enabled = true
            };
        })
        .AddInMemoryProvider(options =>
        {
            options.EnableDeadLetterQueue = true;
            options.DeadLetterQueueCapacity = 2;
            options.MaxRetryAttempts = 3;
            options.RetryDelay = TimeSpan.FromMilliseconds(5);
            options.EnableMessageStore = true;
        });

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<IMessageQueueFactory>();
    }

    [TestMethod]
    public void PublicApi_ReleasedRegistrationAndProviderConstructorSignatures_RemainAvailable()
    {
        var registrationMethod = typeof(InMemoryExtensions).GetMethod(
            nameof(InMemoryExtensions.AddInMemoryProvider),
            [typeof(IMessageQueueBuilder), typeof(Action<InMemoryOptions>)]);
        var providerConstructor = typeof(InMemoryProvider).GetConstructor(
            [typeof(IServiceProvider), typeof(ILogger<InMemoryProvider>)]);

        Assert.IsNotNull(registrationMethod);
        Assert.IsNotNull(providerConstructor);
        // Intentional reflection name: verify a released obsolete member without compile-time use or CS0618.
        var priorityProperty = typeof(InMemoryOptions).GetProperty("EnablePriority");
        Assert.IsNotNull(priorityProperty);
        Assert.IsNotNull(priorityProperty.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
        var provider = _serviceProvider.GetServices<IMessageQueueProvider>()
            .Single(candidate => candidate.ProviderName == "InMemory");
        Assert.IsFalse(provider.Capabilities.SupportsPriority);
        Assert.IsNull(provider.Capabilities.MaxPriorityLevel);
    }

    [TestMethod]
    public async Task CreateAsync_CallerTokenAlreadyCanceled_ThrowsOperationCanceledException()
    {
        var provider = _serviceProvider.GetServices<IMessageQueueProvider>()
            .Single(candidate => candidate.ProviderName == "InMemory");
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => provider.CreateAsync("canceled", cancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task DisposeAsync_ConcurrentQueueAndSubscriptionCalls_CompleteOnce()
    {
        var queue = await _factory.CreateQueueAsync("inmemory");
        var subscription = await queue.SubscribeAsync<TestMessage>(
            "dispose-topic",
            _ => Task.FromResult(MessageResult.Success()));

        await Task.WhenAll(DisposeAsync(subscription), DisposeAsync(subscription));
        await Task.WhenAll(DisposeAsync(queue), DisposeAsync(queue));
    }

    private static async Task DisposeAsync(IAsyncDisposable disposable) => await disposable.DisposeAsync();

    [TestMethod]
    public void AddInMemoryProvider_ConfigurationSectionPresent_BindsTypedOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{InMemoryOptions.ConfigurationSectionName}:ChannelCapacity"] = "25",
                [$"{InMemoryOptions.ConfigurationSectionName}:MaxRetryAttempts"] = "4",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMessageQueue(_ => { }).AddInMemoryProvider();
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<InMemoryOptions>>().Value;

        Assert.AreEqual(25, options.ChannelCapacity);
        Assert.AreEqual(4, options.MaxRetryAttempts);
    }

    [TestMethod]
    public async Task PublishAsync_MessagePublished_HandlerReceivesMessage()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var testMessage = new TestMessage { Id = 1, Content = "Hello" };
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();

        // Act
        await bus.SubscribeAsync<TestMessage>("test-topic", async context =>
        {
            receivedMessages.Add(context.Envelope.Payload);
            taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        });

        var messageId = await bus.PublishAsync("test-topic", testMessage);

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(string.IsNullOrEmpty(messageId));
        Assert.AreEqual(1, receivedMessages.Count);
        Assert.AreEqual(1, receivedMessages[0].Id);
        Assert.AreEqual("Hello", receivedMessages[0].Content);
    }

    [TestMethod]
    public async Task SubscribeAsync_SharedConsumerGroup_DistributesMessages()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var consumer1Messages = new List<int>();
        var consumer2Messages = new List<int>();
        var consumer3Messages = new List<int>();
        var receivedCount = 0;
        var taskCompletionSource = new TaskCompletionSource<bool>();

        // Subscribe 3 consumers with SAME consumer group (competing)
        await bus.SubscribeAsync<TestMessage>("test-topic", async context =>
        {
            consumer1Messages.Add(context.Envelope.Payload.Id);
            if (Interlocked.Increment(ref receivedCount) == 10)
                taskCompletionSource.SetResult(true);
            await Task.Delay(10); // Simulate processing
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "workers" });

        await bus.SubscribeAsync<TestMessage>("test-topic", async context =>
        {
            consumer2Messages.Add(context.Envelope.Payload.Id);
            if (Interlocked.Increment(ref receivedCount) == 10)
                taskCompletionSource.SetResult(true);
            await Task.Delay(10);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "workers" });

        await bus.SubscribeAsync<TestMessage>("test-topic", async context =>
        {
            consumer3Messages.Add(context.Envelope.Payload.Id);
            if (Interlocked.Increment(ref receivedCount) == 10)
                taskCompletionSource.SetResult(true);
            await Task.Delay(10);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "workers" });

        // Act - Publish 10 messages
        for (int i = 0; i < 10; i++)
        {
            await bus.PublishAsync("test-topic", new TestMessage { Id = i, Content = $"Message {i}" });
        }

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // All messages should be received
        var totalMessages = consumer1Messages.Count + consumer2Messages.Count + consumer3Messages.Count;
        Assert.AreEqual(10, totalMessages);

        // Messages should be distributed (load balanced)
        Assert.IsTrue(consumer1Messages.Count > 0, "consumer 1 should get some messages");
        Assert.IsTrue(consumer2Messages.Count > 0, "consumer 2 should get some messages");
        Assert.IsTrue(consumer3Messages.Count > 0, "consumer 3 should get some messages");
    }

    [TestMethod]
    public async Task SubscribeAsync_DefaultConsumerGroups_BroadcastsMessage()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var inventoryReceived = false;
        var shippingReceived = false;
        var emailReceived = false;
        var taskCompletionSource = new TaskCompletionSource<bool>();
        var receivedCount = 0;

        // Subscribe 3 consumers with DIFFERENT consumer groups (broadcast)
        await bus.SubscribeAsync<TestMessage>("orders", async context =>
        {
            inventoryReceived = true;
            if (Interlocked.Increment(ref receivedCount) == 3)
                taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "inventory-service" });

        await bus.SubscribeAsync<TestMessage>("orders", async context =>
        {
            shippingReceived = true;
            if (Interlocked.Increment(ref receivedCount) == 3)
                taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "shipping-service" });

        await bus.SubscribeAsync<TestMessage>("orders", async context =>
        {
            emailReceived = true;
            if (Interlocked.Increment(ref receivedCount) == 3)
                taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "email-service" });

        // Act
        await bus.PublishAsync("orders", new TestMessage { Id = 123, Content = "Order Created" });

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(inventoryReceived, "inventory service should receive message");
        Assert.IsTrue(shippingReceived, "shipping service should receive message");
        Assert.IsTrue(emailReceived, "email service should receive message");
    }

    [TestMethod]
    public async Task SubscribeAsync_HandlerRequestsRetry_RetriesMessage()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var attemptCount = 0;
        var taskCompletionSource = new TaskCompletionSource<bool>();

        await bus.SubscribeAsync<TestMessage>("test-topic", async context =>
        {
            attemptCount++;

            if (attemptCount < 3)
            {
                // Fail first 2 attempts
                return MessageResult.Retry("Simulated failure");
            }

            // Succeed on 3rd attempt
            taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        });

        // Act
        await bus.PublishAsync("test-topic", new TestMessage { Id = 1, Content = "Test" });

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(3, attemptCount, "should retry twice then succeed");
    }

    [TestMethod]
    public async Task SubscribeAsync_HandlerThrowsWithDefaultPolicy_DoesNotRetry()
    {
        var bus = await _factory.CreateQueueAsync("inmemory");
        var firstAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptCount = 0;

        await using var subscription = await bus.SubscribeAsync<TestMessage>(
            "exception-default",
            _ =>
            {
                Interlocked.Increment(ref attemptCount);
                firstAttempt.TrySetResult(true);
                return Task.FromException<MessageResult>(new InvalidOperationException("poison"));
            },
            new SubscribeOptions { ConsumerGroup = "exception-default-consumer" });

        await bus.PublishAsync("exception-default", new TestMessage { Id = 1, Content = "Test" });
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.AreEqual(1, attemptCount, "RequeueOnException defaults to false.");
    }

    [TestMethod]
    public async Task SubscribeAsync_HandlerThrowsWithRequeueEnabled_RetriesWithinFiniteCap()
    {
        var bus = await _factory.CreateQueueAsync("inmemory");
        var succeeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptCount = 0;

        await using var subscription = await bus.SubscribeAsync<TestMessage>(
            "exception-requeue",
            _ =>
            {
                if (Interlocked.Increment(ref attemptCount) == 1)
                {
                    return Task.FromException<MessageResult>(new InvalidOperationException("transient"));
                }

                succeeded.TrySetResult(true);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions
            {
                ConsumerGroup = "exception-requeue-consumer",
                RequeueOnException = true,
                MaxDeliveryCount = 2,
                RetryPolicy = CreateRetryPolicy(maxRetryAttempts: 10, initialDelay: TimeSpan.FromMilliseconds(5))
            });

        await bus.PublishAsync("exception-requeue", new TestMessage { Id = 2, Content = "Test" });
        await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(2, attemptCount);
    }

    [TestMethod]
    public async Task SubscribeAsync_ExplicitMaxDeliveryCount_OverridesPolicyAttemptCount()
    {
        var bus = await _factory.CreateQueueAsync("inmemory");
        var capReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptCount = 0;

        await using var subscription = await bus.SubscribeAsync<TestMessage>(
            "explicit-delivery-cap",
            _ =>
            {
                if (Interlocked.Increment(ref attemptCount) == 2)
                {
                    capReached.TrySetResult(true);
                }

                return Task.FromResult(MessageResult.Retry("transient"));
            },
            new SubscribeOptions
            {
                ConsumerGroup = "explicit-delivery-cap-consumer",
                MaxDeliveryCount = 2,
                RetryPolicy = CreateRetryPolicy(maxRetryAttempts: 10, initialDelay: TimeSpan.FromMilliseconds(5))
            });

        await bus.PublishAsync("explicit-delivery-cap", new TestMessage { Id = 3, Content = "Test" });
        await capReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.AreEqual(2, attemptCount);
    }

    [TestMethod]
    public async Task SubscribeAsync_ExponentialRetryPolicy_AppliesDelayAndFiniteCap()
    {
        var bus = await _factory.CreateQueueAsync("inmemory");
        var succeeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();
        var deliveryTimes = new List<TimeSpan>();

        await using var subscription = await bus.SubscribeAsync<TestMessage>(
            "exponential-backoff",
            _ =>
            {
                deliveryTimes.Add(stopwatch.Elapsed);
                if (deliveryTimes.Count < 3)
                {
                    return Task.FromResult(MessageResult.Retry("transient"));
                }

                succeeded.TrySetResult(true);
                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions
            {
                ConsumerGroup = "exponential-backoff-consumer",
                RetryPolicy = new RetryPolicy
                {
                    MaxRetryAttempts = 2,
                    InitialDelay = TimeSpan.FromMilliseconds(40),
                    MaxDelay = TimeSpan.FromMilliseconds(200),
                    BackoffMultiplier = 3,
                    UseExponentialBackoff = true,
                    UseJitter = false
                }
            });

        await bus.PublishAsync("exponential-backoff", new TestMessage { Id = 4, Content = "Test" });
        await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(3, deliveryTimes.Count, "Two retries imply three finite deliveries.");
        Assert.IsTrue(
            deliveryTimes[1] - deliveryTimes[0] >= TimeSpan.FromMilliseconds(25),
            "The first retry should use InitialDelay.");
        Assert.IsTrue(
            deliveryTimes[2] - deliveryTimes[1] >= TimeSpan.FromMilliseconds(90),
            "The second retry should use exponential backoff.");
    }

    private static RetryPolicy CreateRetryPolicy(int maxRetryAttempts, TimeSpan initialDelay)
    {
        return new RetryPolicy
        {
            MaxRetryAttempts = maxRetryAttempts,
            InitialDelay = initialDelay,
            MaxDelay = initialDelay,
            BackoffMultiplier = 1,
            UseExponentialBackoff = false,
            UseJitter = false
        };
    }

    [TestMethod]
    public async Task PauseAsync_ThenResumeAsync_ControlsMessageFlow()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var receivedMessages = new List<int>();
        var subscription = await bus.SubscribeAsync<TestMessage>("test-topic", async context =>
        {
            receivedMessages.Add(context.Envelope.Payload.Id);
            return MessageResult.Success();
        });

        // Act - Send message 1
        await bus.PublishAsync("test-topic", new TestMessage { Id = 1, Content = "First" });
        await Task.Delay(100);

        // Pause
        await subscription.PauseAsync();
        Assert.IsFalse(subscription.IsActive);

        // Send message 2 (should not be processed while paused)
        await bus.PublishAsync("test-topic", new TestMessage { Id = 2, Content = "Second" });
        await Task.Delay(100);

        // Resume
        await subscription.ResumeAsync();
        Assert.IsTrue(subscription.IsActive);
        await Task.Delay(100);

        // Assert
        Assert.IsTrue(receivedMessages.Contains(1), "first message should be received");
        Assert.IsTrue(receivedMessages.Contains(2), "second message should be received after resume");
    }

    [TestMethod]
    public async Task PublishBatchAsync_MultipleMessages_PublishesEveryMessage()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();

        await bus.SubscribeAsync<TestMessage>("test-topic", async context =>
        {
            receivedMessages.Add(context.Envelope.Payload);
            if (receivedMessages.Count >= 5)
                taskCompletionSource.TrySetResult(true);
            return MessageResult.Success();
        });

        // Act
        var messages = Enumerable.Range(1, 5)
            .Select(i => new TestMessage { Id = i, Content = $"Message {i}" })
            .ToList();

        var messageIds = await bus.PublishBatchAsync("test-topic", messages);

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(5, messageIds.Count());
        Assert.AreEqual(5, receivedMessages.Count);
    }

    [TestMethod]
    public async Task IsHealthyAsync_ActiveQueue_ReturnsTrue()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");

        // Act
        var isHealthy = await bus.IsHealthyAsync();

        // Assert
        // Assert
        Assert.IsTrue(isHealthy);
    }

    [TestMethod]
    public async Task SubscribeAsync_SingleWordRoutingWildcard_MatchesOneWord()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();

        // Subscribe to "orders.*.created" pattern
        await bus.SubscribeAsync<TestMessage>("routing-topic", async context =>
        {
            receivedMessages.Add(context.Envelope.Payload);
            if (receivedMessages.Count >= 2)
                taskCompletionSource.TrySetResult(true);
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "wildcard-consumer",
            RoutingPattern = "orders.*.created"
        });

        // Act - Publish with different routing keys
        await bus.PublishAsync("routing-topic",
            new TestMessage { Id = 1, Content = "US Order" },
            new PublishOptions { RoutingKey = "orders.us.created" }); // MATCH

        await bus.PublishAsync("routing-topic",
            new TestMessage { Id = 2, Content = "EU Order" },
            new PublishOptions { RoutingKey = "orders.eu.created" }); // MATCH

        await bus.PublishAsync("routing-topic",
            new TestMessage { Id = 3, Content = "No region" },
            new PublishOptions { RoutingKey = "orders.created" }); // NO MATCH

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, receivedMessages.Count);
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 1));
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 2));
    }

    [TestMethod]
    public async Task SubscribeAsync_MultiWordRoutingWildcard_MatchesMultipleWords()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();

        // Subscribe to "orders.#" pattern
        await bus.SubscribeAsync<TestMessage>("routing-multi", async context =>
        {
            receivedMessages.Add(context.Envelope.Payload);
            if (receivedMessages.Count >= 3)
                taskCompletionSource.TrySetResult(true);
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "multi-wildcard-consumer",
            RoutingPattern = "orders.#"
        });

        // Act
        await bus.PublishAsync("routing-multi",
            new TestMessage { Id = 1, Content = "Just orders" },
            new PublishOptions { RoutingKey = "orders" }); // MATCH

        await bus.PublishAsync("routing-multi",
            new TestMessage { Id = 2, Content = "One level" },
            new PublishOptions { RoutingKey = "orders.created" }); // MATCH

        await bus.PublishAsync("routing-multi",
            new TestMessage { Id = 3, Content = "Three levels" },
            new PublishOptions { RoutingKey = "orders.us.electronics.created" }); // MATCH

        await bus.PublishAsync("routing-multi",
            new TestMessage { Id = 4, Content = "Inventory" },
            new PublishOptions { RoutingKey = "inventory.updated" }); // NO MATCH

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(3, receivedMessages.Count);
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 1));
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 2));
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 3));
    }

    [TestMethod]
    public async Task SubscribeAsync_NonmatchingRoutingPattern_DoesNotDispatchMessage()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var receivedMessages = new List<TestMessage>();

        // Subscribe to "orders.*" only
        await bus.SubscribeAsync<TestMessage>("no-match", async context =>
        {
            receivedMessages.Add(context.Envelope.Payload);
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "orders-consumer",
            RoutingPattern = "orders.*"
        });

        // Act
        await bus.PublishAsync("no-match",
            new TestMessage { Id = 1, Content = "Inventory" },
            new PublishOptions { RoutingKey = "inventory.updated" }); // NO MATCH

        await bus.PublishAsync("no-match",
            new TestMessage { Id = 2, Content = "Order" },
            new PublishOptions { RoutingKey = "orders.created" }); // MATCH

        await Task.Delay(1000); // Wait for processing

        // Assert - Should only receive matching message
        Assert.AreEqual(1, receivedMessages.Count);
        Assert.AreEqual(2, receivedMessages[0].Id);
    }

    [TestMethod]
    public async Task SubscribeAsync_MultipleRoutingPatterns_MatchesEveryPattern()
    {
        // Arrange
        var bus = await _factory.CreateQueueAsync("inmemory");
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();

        // Subscribe with multiple patterns
        await bus.SubscribeAsync<TestMessage>("multi-patterns", async context =>
        {
            receivedMessages.Add(context.Envelope.Payload);
            if (receivedMessages.Count >= 3)
                taskCompletionSource.TrySetResult(true);
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "multi-pattern-consumer",
            RoutingPatterns = new List<string>
            {
                "orders.*.created",
                "orders.*.updated",
                "inventory.*.low_stock"
            }
        });

        // Act
        await bus.PublishAsync("multi-patterns",
            new TestMessage { Id = 1, Content = "Order created" },
            new PublishOptions { RoutingKey = "orders.us.created" }); // Match pattern 1

        await bus.PublishAsync("multi-patterns",
            new TestMessage { Id = 2, Content = "Order updated" },
            new PublishOptions { RoutingKey = "orders.eu.updated" }); // Match pattern 2

        await bus.PublishAsync("multi-patterns",
            new TestMessage { Id = 3, Content = "Low stock" },
            new PublishOptions { RoutingKey = "inventory.warehouse.low_stock" }); // Match pattern 3

        await bus.PublishAsync("multi-patterns",
            new TestMessage { Id = 4, Content = "No match" },
            new PublishOptions { RoutingKey = "shipping.delivered" }); // No match

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(3, receivedMessages.Count);
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 1));
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 2));
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 3));
    }

    [TestMethod]
    public async Task SubscribeAsync_ParentTokenCanceledAfterSetup_CancelsActiveHandler()
    {
        var bus = await _factory.CreateQueueAsync("inmemory");
        using var lifetimeTokenSource = new CancellationTokenSource();
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await bus.SubscribeAsync<TestMessage>(
            "lifetime-cancellation",
            async context =>
            {
                handlerStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    handlerCanceled.TrySetResult();
                    throw;
                }

                return MessageResult.Success();
            },
            new SubscribeOptions { ConsumerGroup = "lifetime-cancellation" },
            lifetimeTokenSource.Token);

        await bus.PublishAsync(
            "lifetime-cancellation",
            new TestMessage { Id = 10, Content = "cancel me" });
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await lifetimeTokenSource.CancelAsync();

        await handlerCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertEventuallyAsync(() => !subscription.IsActive, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task SubscribeAsync_BoundedGroupCanceledWithQueuedWork_RedeliversEveryUnfinishedMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(options =>
        {
            options.Providers["bounded"] = new ProviderConfiguration
            {
                Type = "InMemory",
                Enabled = true,
            };
        }).AddInMemoryProvider(options =>
        {
            options.ChannelCapacity = 1;
            options.RetryDelay = TimeSpan.FromMilliseconds(5);
        });
        await using var serviceProvider = services.BuildServiceProvider();
        var queue = await serviceProvider
            .GetRequiredService<IMessageQueueFactory>()
            .CreateQueueAsync("bounded");
        using var lifetimeTokenSource = new CancellationTokenSource();
        var firstHandlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstSubscription = await queue.SubscribeAsync<TestMessage>(
            "bounded-cancellation",
            async context =>
            {
                firstHandlerStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                return MessageResult.Success();
            },
            new SubscribeOptions
            {
                ConsumerGroup = "bounded-workers",
                MaxConcurrency = 1,
            },
            lifetimeTokenSource.Token);

        await queue.PublishAsync("bounded-cancellation", new TestMessage { Id = 1 });
        await firstHandlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await queue.PublishAsync("bounded-cancellation", new TestMessage { Id = 2 });

        // Completion proves message 2 has been removed from the one-slot publisher buffer and is
        // waiting behind the active handler while message 3 now occupies that slot.
        await queue.PublishAsync("bounded-cancellation", new TestMessage { Id = 3 });

        await lifetimeTokenSource.CancelAsync();
        await firstSubscription.DisposeAsync();

        var redelivered = new HashSet<int>();
        var allRedelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var replacement = await queue.SubscribeAsync<TestMessage>(
            "bounded-cancellation",
            context =>
            {
                lock (redelivered)
                {
                    redelivered.Add(context.Envelope.Payload.Id);
                    if (redelivered.Count == 3)
                    {
                        allRedelivered.TrySetResult();
                    }
                }

                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { ConsumerGroup = "bounded-workers" });

        await allRedelivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, redelivered.ToArray());
    }

    [TestMethod]
    public async Task SubscribeAsync_PausedAfterDequeueThenCanceled_RedeliversDequeuedMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(options =>
        {
            options.Providers["paused"] = new ProviderConfiguration
            {
                Type = "InMemory",
                Enabled = true,
            };
        }).AddInMemoryProvider(options => options.ChannelCapacity = 1);
        await using var serviceProvider = services.BuildServiceProvider();
        var queue = await serviceProvider
            .GetRequiredService<IMessageQueueFactory>()
            .CreateQueueAsync("paused");
        using var lifetimeTokenSource = new CancellationTokenSource();
        var subscription = await queue.SubscribeAsync<TestMessage>(
            "paused-cancellation",
            _ => Task.FromResult(MessageResult.Success()),
            new SubscribeOptions { ConsumerGroup = "paused-workers" },
            lifetimeTokenSource.Token);

        await subscription.PauseAsync();
        await queue.PublishAsync("paused-cancellation", new TestMessage { Id = 41 });

        // With a one-slot publisher buffer, completion of the second publish proves that the
        // paused reader dequeued message 41 and is holding it behind the pause gate.
        await queue.PublishAsync("paused-cancellation", new TestMessage { Id = 42 });
        await lifetimeTokenSource.CancelAsync();
        await subscription.DisposeAsync();

        var redelivered = new HashSet<int>();
        var allRedelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var replacement = await queue.SubscribeAsync<TestMessage>(
            "paused-cancellation",
            context =>
            {
                lock (redelivered)
                {
                    redelivered.Add(context.Envelope.Payload.Id);
                    if (redelivered.Count == 2)
                    {
                        allRedelivered.TrySetResult();
                    }
                }

                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { ConsumerGroup = "paused-workers" });

        await allRedelivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEquivalent(new[] { 41, 42 }, redelivered.ToArray());
    }

    [TestMethod]
    public async Task DeadLetterStore_CapacityExceeded_RetainsInspectableNewestMessages()
    {
        var queue = await _factory.CreateQueueAsync("inmemory");
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejectionCount = 0;
        await using var subscription = await queue.SubscribeAsync<TestMessage>(
            "bounded-dead-letters",
            _ =>
            {
                if (Interlocked.Increment(ref rejectionCount) == 3)
                {
                    rejected.TrySetResult();
                }

                return Task.FromResult(MessageResult.Reject("poison"));
            },
            new SubscribeOptions { ConsumerGroup = "dead-letter-workers" });

        for (var id = 1; id <= 3; id++)
        {
            await queue.PublishAsync(
                "bounded-dead-letters",
                new TestMessage { Id = id },
                new PublishOptions { MessageId = $"message-{id}" });
        }

        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertEventuallyAsync(
            () => _serviceProvider.GetRequiredService<IInMemoryDeadLetterStore>()
                .GetSnapshot("inmemory").Count == 2,
            TimeSpan.FromSeconds(5));

        var snapshot = _serviceProvider.GetRequiredService<IInMemoryDeadLetterStore>()
            .GetSnapshot("inmemory");
        CollectionAssert.AreEqual(
            new[] { "message-2", "message-3" },
            snapshot.Select(message => message.MessageId).ToArray());
    }

    [TestMethod]
    public async Task PublishAsync_AcceptedDelayedMessageCallerTokenCanceled_StillDeliversMessage()
    {
        var bus = await _factory.CreateQueueAsync("inmemory");
        var received = new TaskCompletionSource<TestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await bus.SubscribeAsync<TestMessage>(
            "accepted-delay",
            context =>
            {
                received.TrySetResult(context.Envelope.Payload);
                return Task.FromResult(MessageResult.Success());
            });
        using var publishTokenSource = new CancellationTokenSource();

        await bus.PublishAsync(
            "accepted-delay",
            new TestMessage { Id = 11, Content = "accepted" },
            new PublishOptions { DeliveryDelay = TimeSpan.FromMilliseconds(100) },
            publishTokenSource.Token);
        await publishTokenSource.CancelAsync();

        var delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(11, delivered.Id);
    }

    private static async Task AssertEventuallyAsync(Func<bool> assertion, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (assertion())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.IsTrue(assertion(), "The expected state was not reached before the timeout.");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

