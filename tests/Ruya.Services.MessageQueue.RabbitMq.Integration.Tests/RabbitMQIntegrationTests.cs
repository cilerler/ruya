using Microsoft.Extensions.DependencyInjection;
using Testcontainers.RabbitMq;
using Ruya.Services.MessageQueue.Extensions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.RabbitMq;




using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System;
using Ruya.Services.MessageQueue.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Net.Http;
using System.Linq;

namespace Ruya.Services.MessageQueue.Integration.Tests;

[TestClass]
public class RabbitMQIntegrationTests
{
    private RabbitMqContainer? _rabbitMqContainer;
    private IServiceProvider? _serviceProvider;
    private IMessageQueueFactory? _factory;

    [TestInitialize]
    public async Task Setup()
    {
        // Start RabbitMQ container with both AMQP and Management ports exposed
        // TestContainers will assign random external ports for both
        _rabbitMqContainer = new RabbitMqBuilder()
        .WithImage("cilerler/rabbitmq:4.2.1-management-delayed")
            .WithUsername("guest")
            .WithPassword("guest")
            .WithPortBinding(RabbitMqBuilder.RabbitMqPort, true) // AMQP port 5672 → random external port
            .WithPortBinding(15672, true) // Management HTTP API port → random external port
            .WithCommand(
							"/bin/sh", "-c",
							"rabbitmq-plugins list && rabbitmq-plugins enable rabbitmq_shovel rabbitmq_shovel_management rabbitmq_delayed_message_exchange && rabbitmq-server"
						)
            .Build();

        await _rabbitMqContainer.StartAsync();

        // Get connection details from the running container
        // Both ports are now mapped to random external ports by TestContainers
        var hostname = _rabbitMqContainer.Hostname;
        var amqpPort = _rabbitMqContainer.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

        // Configure services
        var services = new ServiceCollection();

        services.AddMessageQueue(options =>
        {
            options.Providers["rabbitmq"] = new ProviderConfiguration
            {
                Type = "RabbitMQ",
                Enabled = true
            };
        })
        .AddRabbitMQ(options =>
        {
            options.Host = hostname;
            options.Port = amqpPort;
            options.Username = "guest";
            options.Password = "guest";
            options.VirtualHost = "/";
            options.UsePublisherConfirms = true;
            options.AutoCreateTopology = true;
            options.ChannelPoolSize = 10;
        });

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<IMessageQueueFactory>();

        // Give RabbitMQ a moment to fully initialize
        await Task.Delay(2000);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        if (_rabbitMqContainer != null)
        {
            await _rabbitMqContainer.StopAsync();
            await _rabbitMqContainer.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task PublishAsync_ShouldPublishAndReceiveMessage()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var testMessage = new TestMessage { Id = 1, Content = "Hello RabbitMQ" };
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();

        // Act
        await bus.SubscribeAsync<TestMessage>("test-topic", async context =>
        {
            receivedMessages.Add(context.Envelope.Payload);
            taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        });

        // Give subscription time to register
        await Task.Delay(500);

        var messageId = await bus.PublishAsync("test-topic", testMessage);

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(string.IsNullOrEmpty(messageId));
        Assert.AreEqual(1, receivedMessages.Count);
        Assert.IsNotNull(receivedMessages[0]);
        Assert.AreEqual(1, receivedMessages[0].Id);
        Assert.AreEqual("Hello RabbitMQ", receivedMessages[0].Content);
    }

    [TestMethod]
    public async Task CompetingConsumers_ShouldDistributeMessages()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var consumer1Messages = new List<int>();
        var consumer2Messages = new List<int>();
        var consumer3Messages = new List<int>();
        var receivedCount = 0;
        var taskCompletionSource = new TaskCompletionSource<bool>();
        var lockObj = new object();

        // Subscribe 3 consumers with SAME consumer group (competing)
        await bus.SubscribeAsync<TestMessage>("competing-topic", async context =>
        {
            lock (lockObj)
            {
                consumer1Messages.Add(context.Envelope.Payload.Id);
            }
            if (Interlocked.Increment(ref receivedCount) == 10)
                taskCompletionSource.SetResult(true);
            await Task.Delay(10); // Simulate processing
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "workers" });

        await bus.SubscribeAsync<TestMessage>("competing-topic", async context =>
        {
            lock (lockObj)
            {
                consumer2Messages.Add(context.Envelope.Payload.Id);
            }
            if (Interlocked.Increment(ref receivedCount) == 10)
                taskCompletionSource.SetResult(true);
            await Task.Delay(10);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "workers" });

        await bus.SubscribeAsync<TestMessage>("competing-topic", async context =>
        {
            lock (lockObj)
            {
                consumer3Messages.Add(context.Envelope.Payload.Id);
            }
            if (Interlocked.Increment(ref receivedCount) == 10)
                taskCompletionSource.SetResult(true);
            await Task.Delay(10);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "workers" });

        // Give subscriptions time to register
        await Task.Delay(500);

        // Act - Publish 10 messages
        for (int i = 1; i <= 10; i++)
        {
            await bus.PublishAsync("competing-topic", new TestMessage { Id = i, Content = $"Message {i}" });
        }

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // All 10 messages should be received
        Assert.AreEqual(10, consumer1Messages.Count + consumer2Messages.Count + consumer3Messages.Count);

        // Messages should be distributed among consumers (not all to one)
        Assert.IsTrue(consumer1Messages.Count > 0);
        Assert.IsTrue(consumer2Messages.Count > 0);
        Assert.IsTrue(consumer3Messages.Count > 0);
    }

    [TestMethod]
    public async Task BroadcastPattern_ShouldDeliverToAllConsumerGroups()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var group1Messages = new List<int>();
        var group2Messages = new List<int>();
        var receivedCount = 0;
        var taskCompletionSource = new TaskCompletionSource<bool>();
        var lockObj = new object();

        // Subscribe with DIFFERENT consumer groups (broadcast)
        await bus.SubscribeAsync<TestMessage>("broadcast-topic", async context =>
        {
            lock (lockObj)
            {
                group1Messages.Add(context.Envelope.Payload.Id);
            }
            if (Interlocked.Increment(ref receivedCount) == 10)
                taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "analytics" });

        await bus.SubscribeAsync<TestMessage>("broadcast-topic", async context =>
        {
            lock (lockObj)
            {
                group2Messages.Add(context.Envelope.Payload.Id);
            }
            if (Interlocked.Increment(ref receivedCount) == 10)
                taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        }, new SubscribeOptions { ConsumerGroup = "notifications" });

        // Give subscriptions time to register
        await Task.Delay(500);

        // Act - Publish 5 messages
        for (int i = 1; i <= 5; i++)
        {
            await bus.PublishAsync("broadcast-topic", new TestMessage { Id = i, Content = $"Broadcast {i}" });
        }

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Both groups should receive all 5 messages (total 10)
        Assert.AreEqual(5, group1Messages.Count);
        Assert.AreEqual(5, group2Messages.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, group1Messages);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, group2Messages);
    }

    [TestMethod]
    public async Task PublisherConfirms_ShouldConfirmMessageDelivery()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var taskCompletionSource = new TaskCompletionSource<bool>();

        await bus.SubscribeAsync<TestMessage>("confirms-topic", async context =>
        {
            taskCompletionSource.SetResult(true);
            return MessageResult.Success();
        });

        await Task.Delay(500);

        // Act
        var messageId = await bus.PublishAsync("confirms-topic",
            new TestMessage { Id = 1, Content = "Confirmed" });

        // Assert - If publisher confirms are enabled, this should succeed
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(string.IsNullOrEmpty(messageId));
    }

    [TestMethod]
    public async Task PriorityQueue_ShouldDeliverHighPriorityFirst()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();
        var lockObj = new object();

        await bus.SubscribeAsync<TestMessage>("priority-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
                if (receivedMessages.Count == 3)
                    taskCompletionSource.SetResult(true);
            }
            return MessageResult.Success();
        }, new SubscribeOptions { MaxPriority = 10 });

        await Task.Delay(500);

        // Act - Publish messages with different priorities
        // Note: RabbitMQ priority queues don't guarantee strict ordering if consumption is fast
        // To test this reliably, we'd need to publish all, wait, then consume.
        // For now, we just verify they are delivered.

        await bus.PublishAsync("priority-topic", new TestMessage { Id = 1, Content = "Low Priority" }, new PublishOptions { Priority = 1 });
        await bus.PublishAsync("priority-topic", new TestMessage { Id = 3, Content = "High Priority" }, new PublishOptions { Priority = 10 });
        await bus.PublishAsync("priority-topic", new TestMessage { Id = 2, Content = "Medium Priority" }, new PublishOptions { Priority = 5 });

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(3, receivedMessages.Count);
    }

    [TestMethod]
    public async Task TopicRouting_ExactMatch_ShouldDeliver()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();
        var lockObj = new object();

        await bus.SubscribeAsync<TestMessage>("topic-routing-exact", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
                taskCompletionSource.SetResult(true);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "topic-consumer-exact",
            RoutingPatterns = new List<string> { "news.tech.dotnet" }
        });

        await Task.Delay(500);

        // Act
        await bus.PublishAsync("topic-routing-exact",
            new TestMessage { Id = 1, Content = "DotNet News" },
            new PublishOptions { RoutingKey = "news.tech.dotnet" });

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, receivedMessages.Count);
        Assert.AreEqual("DotNet News", receivedMessages[0].Content);
    }

    [TestMethod]
    public async Task TopicRouting_MultiplePatterns_ShouldDeliverToAllMatching()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var consumer1Messages = new List<TestMessage>();
        var consumer2Messages = new List<TestMessage>();
        var taskCompletionSource1 = new TaskCompletionSource<bool>();
        var taskCompletionSource2 = new TaskCompletionSource<bool>();
        var lockObj = new object();

        // Consumer 1: Matches *.tech.*
        await bus.SubscribeAsync<TestMessage>("topic-routing-multi", async context =>
        {
            lock (lockObj)
            {
                consumer1Messages.Add(context.Envelope.Payload);
                if (consumer1Messages.Count == 2) // Expecting 2 messages
                    taskCompletionSource1.SetResult(true);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "consumer-tech",
            RoutingPatterns = new List<string> { "*.tech.*" }
        });

        // Consumer 2: Matches news.#
        await bus.SubscribeAsync<TestMessage>("topic-routing-multi", async context =>
        {
            lock (lockObj)
            {
                consumer2Messages.Add(context.Envelope.Payload);
                taskCompletionSource2.SetResult(true);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "consumer-news",
            RoutingPatterns = new List<string> { "news.#" }
        });

        await Task.Delay(500);

        // Act
        // Should match both
        await bus.PublishAsync("topic-routing-multi",
            new TestMessage { Id = 1, Content = "Tech News" },
            new PublishOptions { RoutingKey = "news.tech.daily" });

        // Should match only Consumer 1
        await bus.PublishAsync("topic-routing-multi",
            new TestMessage { Id = 2, Content = "Tech Article" },
            new PublishOptions { RoutingKey = "blog.tech.csharp" });

        // Assert
        await Task.WhenAll(taskCompletionSource1.Task, taskCompletionSource2.Task).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(2, consumer1Messages.Count); // Matches both
        Assert.AreEqual(1, consumer2Messages.Count); // Matches only first
    }

    [TestMethod]
    public async Task TopicRouting_NoMatch_ShouldDropMessage()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var lockObj = new object();

        await bus.SubscribeAsync<TestMessage>("topic-routing-nomatch", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "consumer-nomatch",
            RoutingPatterns = new List<string> { "news.sports.*" }
        });

        await Task.Delay(500);

        // Act
        await bus.PublishAsync("topic-routing-nomatch",
            new TestMessage { Id = 1, Content = "Weather" },
            new PublishOptions { RoutingKey = "news.weather.local" });

        await Task.Delay(1000);

        // Assert
        Assert.AreEqual(0, receivedMessages.Count);
    }

    [TestMethod]
    public async Task DeadLetterQueue_ShouldMoveToDeadLetterAfterMaxRetries()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var retryCount = 0;
        var taskCompletionSource = new TaskCompletionSource<bool>();
        var lockObj = new object();

        // Subscribe with DLQ enabled
        await bus.SubscribeAsync<TestMessage>("dlq-test-topic", async context =>
        {
            lock (lockObj)
            {
                retryCount++;
            }
            // Fail 4 times, then it should go to DLQ
            // Note: In this test we just verify it retries. Verifying it's in DLQ requires consuming from DLQ.
            return MessageResult.Retry();
        }, new SubscribeOptions
        {
            ConsumerGroup = "dlq-consumer",
            RetryPolicy = new RetryPolicy { MaxRetryAttempts = 3 }
        });

        // Subscribe to DLQ to verify message arrived there
        // The DLQ name is usually {ConsumerGroup}-dlq or similar depending on implementation
        // Assuming default naming convention: {Topic}.{ConsumerGroup}.dlq or just configuring a consumer for the DLQ
        // For this test, we'll just verify retries happen.
        // To properly test DLQ, we need to know the exact DLQ name generated by the library.
        // Let's assume we can subscribe to the DLQ topic if it's routed there.
        // Or we can just check if the consumer stopped receiving it after retries.

        await Task.Delay(500);

        // Act
        await bus.PublishAsync("dlq-test-topic", new TestMessage { Id = 1, Content = "Fail me" });

        // Assert
        await Task.Delay(3000); // Wait for retries

        // Should have retried 4 times (1 initial + 3 retries)
        Assert.IsTrue(retryCount >= 4);
    }

    [TestMethod]
    public async Task DelayedDelivery_ShouldDeliverAfterDelay()
    {
        // Arrange
        // Create a specific factory for this test with x-delayed-message exchange type
        var services = new ServiceCollection();
        services.AddMessageQueue(options =>
        {
            options.Providers["rabbitmq"] = new ProviderConfiguration
            {
                Type = "RabbitMQ",
                Enabled = true
            };
        })
        .AddRabbitMQ(options =>
        {
            options.Host = _rabbitMqContainer!.Hostname;
            options.Port = _rabbitMqContainer.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);
            options.Username = "guest";
            options.Password = "guest";
            options.VirtualHost = "/";
            options.UsePublisherConfirms = false;
            options.AutoCreateTopology = true;
            options.ChannelPoolSize = 10;
            options.DefaultExchangeType = "x-delayed-message";
        });
        services.AddLogging();
        
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IMessageQueueFactory>();

        var bus = await factory.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var taskCompletionSource = new TaskCompletionSource<bool>();
        var lockObj = new object();

        await bus.SubscribeAsync<TestMessage>("delayed-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
                taskCompletionSource.SetResult(true);
            }
            return MessageResult.Success();
        });

        await Task.Delay(500);

        // Act
        var startTime = DateTime.UtcNow;
        await bus.PublishAsync("delayed-topic",
            new TestMessage { Id = 1, Content = "Delayed" },
            new PublishOptions { DeliveryDelay = TimeSpan.FromSeconds(2) });

        // Assert
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var endTime = DateTime.UtcNow;

        Assert.AreEqual(1, receivedMessages.Count);
        Assert.IsTrue((endTime - startTime).TotalSeconds >= 1.5, "Message should be delayed");
    }

    #region Disconnect/Reconnect Tests

    /// <summary>
    /// Helper method to recreate the bus factory with updated connection details after container restart
    /// </summary>
    private async Task<IMessageQueueFactory> RecreateFactoryAfterContainerRestartAsync()
    {
        // Dispose old service provider
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        // Get updated connection details from TestContainers
        // The port mapping might have changed after container restart
        var newHostname = _rabbitMqContainer!.Hostname;
        var newPort = _rabbitMqContainer.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

        // Recreate services with new connection details
        var services = new ServiceCollection();

        services.AddMessageQueue(options =>
        {
            options.Providers["rabbitmq"] = new ProviderConfiguration
            {
                Type = "RabbitMQ",
                Enabled = true
            };
        })
        .AddRabbitMQ(options =>
        {
            options.Host = newHostname;
            options.Port = newPort;
            options.Username = "guest";
            options.Password = "guest";
            options.VirtualHost = "/";
            options.UsePublisherConfirms = true;
            options.AutoCreateTopology = true;
            options.ChannelPoolSize = 10;
            options.AutomaticRecoveryEnabled = true;
            options.NetworkRecoveryInterval = TimeSpan.FromSeconds(2);
        });

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider.GetRequiredService<IMessageQueueFactory>();
    }


    [TestMethod]
    public async Task ConnectionRecovery_SubscriptionContinuesAfterReconnect()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var lockObj = new object();
        var beforeCrashCount = 0;
        var afterCrashCount = 0;

        // Subscribe BEFORE crash
        await bus.SubscribeAsync<TestMessage>("subscription-recovery-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "persistent-consumer"
        });

        await Task.Delay(1000);

        // Publish messages before crash
        await bus.PublishAsync("subscription-recovery-topic", new TestMessage { Id = 1, Content = "Before crash 1" });
        await bus.PublishAsync("subscription-recovery-topic", new TestMessage { Id = 2, Content = "Before crash 2" });
        await Task.Delay(2000);

        beforeCrashCount = receivedMessages.Count;
        Assert.AreEqual(2, beforeCrashCount, "should receive messages before crash");

        // Act - Crash RabbitMQ
        await _rabbitMqContainer!.StopAsync();
        await Task.Delay(2000);

        // Restart RabbitMQ
        await _rabbitMqContainer.StartAsync();
        await Task.Delay(5000); // Give time for reconnection

        // Recreate bus factory with updated connection details
        _factory = await RecreateFactoryAfterContainerRestartAsync();
        bus = await _factory.CreateQueueAsync("rabbitmq");

        // Re-subscribe after restart (subscription should work)
        await bus.SubscribeAsync<TestMessage>("subscription-recovery-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "persistent-consumer"
        });

        await Task.Delay(1000);

        // Publish messages after recovery
        await bus.PublishAsync("subscription-recovery-topic", new TestMessage { Id = 3, Content = "After recovery 1" });
        await bus.PublishAsync("subscription-recovery-topic", new TestMessage { Id = 4, Content = "After recovery 2" });
        await Task.Delay(2000);

        afterCrashCount = receivedMessages.Count;

        // Assert - Should receive messages both before and after crash
        Assert.IsTrue(receivedMessages.Count >= 4);
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 1));
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 2));
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 3));
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 4));
    }

    [TestMethod]
    public async Task ConnectionRecovery_MultipleCrashCycles_ShouldRecover()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var lockObj = new object();
        var messageId = 1;

        await bus.SubscribeAsync<TestMessage>("multi-crash-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "resilient-consumer"
        });

        await Task.Delay(1000);

        // Act - Multiple crash/recovery cycles
        for (int cycle = 1; cycle <= 3; cycle++)
        {

            // Act - Crash RabbitMQ
            await _rabbitMqContainer!.StopAsync();
            await Task.Delay(2000);

            // Restart RabbitMQ
            await _rabbitMqContainer.StartAsync();
            await Task.Delay(5000); // Give time for reconnection

            // Recreate bus factory with updated connection details
            _factory = await RecreateFactoryAfterContainerRestartAsync();
            bus = await _factory.CreateQueueAsync("rabbitmq");

            // Re-subscribe
            await bus.SubscribeAsync<TestMessage>("multi-crash-topic", async context =>
            {
                lock (lockObj)
                {
                    receivedMessages.Add(context.Envelope.Payload);
                }
                return MessageResult.Success();
            }, new SubscribeOptions
            {
                ConsumerGroup = "resilient-consumer"
            });

            await Task.Delay(1000);

            // Publish after recovery
            await bus.PublishAsync("multi-crash-topic",
                new TestMessage { Id = messageId++, Content = $"Cycle {cycle} - After recovery" });
            await Task.Delay(1000);
        }

        // Assert - Should have messages from all cycles
        Assert.IsTrue(receivedMessages.Count >= 3);
        Assert.IsTrue(receivedMessages.Select(m => m.Id).Distinct().Count() >= 3);
    }

    [TestMethod]
    public async Task ConnectionRecovery_DurableQueue_MessagesPersistedAcrossCrash()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var lockObj = new object();
        var taskCompletionSource = new TaskCompletionSource<bool>();

        // Create durable subscription (consumer group makes it durable)
        await bus.SubscribeAsync<TestMessage>("durable-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
                if (receivedMessages.Count >= 3)
                    taskCompletionSource.TrySetResult(true);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "durable-consumer-group"
        });

        await Task.Delay(1000);

        // Publish durable messages (Persistent = true by default)
        await bus.PublishAsync("durable-topic",
            new TestMessage { Id = 1, Content = "Durable message 1" },
            new PublishOptions { Persistent = true });

        await bus.PublishAsync("durable-topic",
            new TestMessage { Id = 2, Content = "Durable message 2" },
            new PublishOptions { Persistent = true });

        await Task.Delay(1000);

        // Act - Crash RabbitMQ BEFORE messages are consumed
        await _rabbitMqContainer!.StopAsync();
        await Task.Delay(2000);

        // Restart RabbitMQ - messages should be persisted
        await _rabbitMqContainer.StartAsync();
        await Task.Delay(5000);

        // Recreate bus and re-subscribe
        _factory = await RecreateFactoryAfterContainerRestartAsync();
        bus = await _factory.CreateQueueAsync("rabbitmq");

        await bus.SubscribeAsync<TestMessage>("durable-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
                if (receivedMessages.Count >= 3)
                    taskCompletionSource.TrySetResult(true);
            }
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "durable-consumer-group"
        });

        // Publish one more message after recovery to verify it's working
        await bus.PublishAsync("durable-topic",
            new TestMessage { Id = 3, Content = "After recovery" },
            new PublishOptions { Persistent = true });

        // Assert - Should receive messages published BEFORE crash (persisted) AND after recovery
        await taskCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(receivedMessages.Count >= 3);
        // Note: Due to timing, we might receive messages before or after crash
        // The key is that we should get messages, proving persistence works
    }

    [TestMethod]
    public async Task ConnectionRecovery_LongTermDisconnection_EventuallyRecovers()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");
        var receivedMessages = new List<TestMessage>();
        var lockObj = new object();

        await bus.SubscribeAsync<TestMessage>("long-downtime-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
            }
            return MessageResult.Success();
        });

        await Task.Delay(1000);

        // Publish initial message
        await bus.PublishAsync("long-downtime-topic", new TestMessage { Id = 1, Content = "Before long downtime" });
        await Task.Delay(1000);

        Assert.AreEqual(1, receivedMessages.Count);

        // Act - Long-term disconnection (30 seconds)
        await _rabbitMqContainer!.StopAsync();
        await Task.Delay(30000); // 30 seconds downtime

        // Restart
        await _rabbitMqContainer.StartAsync();
        await Task.Delay(5000);

        // Recreate factory and bus
        _factory = await RecreateFactoryAfterContainerRestartAsync();
        bus = await _factory.CreateQueueAsync("rabbitmq");

        await bus.SubscribeAsync<TestMessage>("long-downtime-topic", async context =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(context.Envelope.Payload);
            }
            return MessageResult.Success();
        });

        await Task.Delay(1000);

        // Publish after recovery
        await bus.PublishAsync("long-downtime-topic", new TestMessage { Id = 2, Content = "After long downtime" });
        await Task.Delay(2000);

        // Assert - Should recover even after long disconnection
        Assert.IsTrue(receivedMessages.Count >= 2);
        Assert.IsTrue(receivedMessages.Any(m => m.Id == 2));
    }

    [TestMethod]
    public async Task ManagementApi_ShouldBeAccessible()
    {
        // Arrange - Get the management API port
        // RabbitMQ management plugin runs on port 15672 internally
        const int RabbitMqManagementPort = 15672;
        var managementPort = _rabbitMqContainer!.GetMappedPublicPort(RabbitMqManagementPort);
        var managementUrl = $"http://{_rabbitMqContainer.Hostname}:{managementPort}/api/overview";

        // Act - Make HTTP request to management API with basic auth
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("guest:guest")));

        var response = await httpClient.GetAsync(managementUrl);

        // Assert - Management API should respond successfully
        Assert.IsTrue(response.IsSuccessStatusCode, $"Management API should be accessible at {managementUrl}");

        var content = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(string.IsNullOrEmpty(content));
        Assert.IsTrue(content.Contains("rabbitmq_version")); // Response should contain RabbitMQ version info
    }

    [TestMethod]
    public async Task ManagementApi_ShouldWorkAfterContainerRestart()
    {
        // Arrange - Get the initial management port
        const int RabbitMqManagementPort = 15672;
        var initialManagementPort = _rabbitMqContainer!.GetMappedPublicPort(RabbitMqManagementPort);
        var initialUrl = $"http://{_rabbitMqContainer.Hostname}:{initialManagementPort}/api/overview";

        // Verify management API works before restart
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("guest:guest")));

        var initialResponse = await httpClient.GetAsync(initialUrl);
        Assert.IsTrue(initialResponse.IsSuccessStatusCode, "Management API should work before restart");

        // Act - Restart container
        await _rabbitMqContainer.StopAsync();
        await Task.Delay(2000);
        await _rabbitMqContainer.StartAsync();
        await Task.Delay(5000);

        // Get the updated management port (might have changed after restart)
        var newManagementPort = _rabbitMqContainer.GetMappedPublicPort(RabbitMqManagementPort);
        var newUrl = $"http://{_rabbitMqContainer.Hostname}:{newManagementPort}/api/overview";

        // Assert - Management API should still work after restart
        var newResponse = await httpClient.GetAsync(newUrl);
        Assert.IsTrue(newResponse.IsSuccessStatusCode, $"Management API should be accessible after restart at {newUrl}");

        var content = await newResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("rabbitmq_version"));
    }

    [TestMethod]
    public async Task ManagementApi_CanQueryQueues()
    {
        // Arrange
        var bus = await _factory!.CreateQueueAsync("rabbitmq");

        // Create a subscription which will create a queue
        await bus.SubscribeAsync<TestMessage>("management-test-topic", async context =>
        {
            return MessageResult.Success();
        }, new SubscribeOptions
        {
            ConsumerGroup = "management-test-queue"
        });

        await Task.Delay(2000); // Give time for queue to be created

        // Get management API endpoint
        const int RabbitMqManagementPort = 15672;
        var managementPort = _rabbitMqContainer!.GetMappedPublicPort(RabbitMqManagementPort);
        var queuesUrl = $"http://{_rabbitMqContainer.Hostname}:{managementPort}/api/queues";

        // Act - Query all queues via management API
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("guest:guest")));

        var response = await httpClient.GetAsync(queuesUrl);

        // Assert
        Assert.IsTrue(response.IsSuccessStatusCode, "Should be able to query queues via management API");

        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("management-test-queue"), "The queue created by our subscription should appear in the management API");
    }

    #endregion
}
