using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
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

namespace Ruya.Services.MessageQueue.Integration.Tests;

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
            options.MaxRetryAttempts = 3;
            options.EnableMessageStore = true;
        });

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<IMessageQueueFactory>();
    }

    [TestMethod]
    public async Task PublishAsync_ShouldPublishMessage()
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
    public async Task CompetingConsumers_ShouldDistributeMessages()
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
    public async Task BroadcastPattern_AllConsumersReceiveMessage()
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
    public async Task MessageRetry_ShouldRetryOnFailure()
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
    public async Task PauseAndResume_ShouldControlMessageFlow()
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
    public async Task BatchPublish_ShouldPublishMultipleMessages()
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
    public async Task HealthCheck_ShouldReturnHealthy()
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
    public async Task TopicRouting_SingleWildcard_ShouldMatchOneWord()
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
    public async Task TopicRouting_MultiWildcard_ShouldMatchMultipleWords()
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
    public async Task TopicRouting_NoMatch_ShouldDropMessage()
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
    public async Task TopicRouting_MultiplePatterns_ShouldMatchAll()
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

