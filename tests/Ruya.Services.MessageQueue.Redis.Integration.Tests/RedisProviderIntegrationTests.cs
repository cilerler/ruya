using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Extensions;
using Testcontainers.Redis;

namespace Ruya.Services.MessageQueue.Redis.Integration.Tests;

[TestClass]
public sealed class RedisProviderIntegrationTests
{
    private static RedisContainer? _redis;
    private ServiceProvider _services = null!;
    private IMessageQueue _queue = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _redis = new RedisBuilder("redis:7-alpine")
            .Build();
        await _redis.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessageQueue(options =>
        {
            options.Providers["redis"] = new ProviderConfiguration
            {
                Type = "Redis",
                Enabled = true,
            };
        }).AddRedis(options =>
        {
            options.ConnectionString = _redis!.GetConnectionString();
            options.UsePubSub = true;
            options.UseStreams = false;
        });

        _services = services.BuildServiceProvider();
        _queue = await _services.GetRequiredService<IMessageQueueFactory>().CreateQueueAsync("redis");
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await _queue.DisposeAsync();
        await _services.DisposeAsync();
    }

    [TestMethod]
    public async Task IsHealthyAsync_BeforeFirstPublish_ConnectsAndReturnsTrue()
    {
        Assert.IsTrue(await _queue.IsHealthyAsync());
    }

    [TestMethod]
    public async Task PublishAsync_LiteralSubscription_DeliversMessage()
    {
        var topic = $"literal-{Guid.NewGuid():N}";
        var received = new TaskCompletionSource<RedisTestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await _queue.SubscribeAsync<RedisTestMessage>(
            topic,
            context =>
            {
                received.TrySetResult(context.Envelope.Payload);
                return Task.FromResult(MessageResult.Success());
            });

        await _queue.PublishAsync(topic, new RedisTestMessage { Id = 1, Content = "hello" });

        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, message.Id);
    }

    [TestMethod]
    public async Task SubscribeAsync_MultipleRoutingPatterns_DeliversOnlyMatchingMessages()
    {
        var topic = $"routing-{Guid.NewGuid():N}";
        var received = new ConcurrentBag<int>();
        var twoMessages = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await _queue.SubscribeAsync<RedisTestMessage>(
            topic,
            context =>
            {
                received.Add(context.Envelope.Payload.Id);
                if (received.Count == 2)
                {
                    twoMessages.TrySetResult();
                }

                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions
            {
                RoutingPatterns = ["orders.*.created", "inventory.#"],
            });

        await _queue.PublishAsync(topic, new RedisTestMessage { Id = 1 }, new PublishOptions { RoutingKey = "orders.us.created" });
        await _queue.PublishAsync(topic, new RedisTestMessage { Id = 2 }, new PublishOptions { RoutingKey = "inventory.warehouse.low" });
        await _queue.PublishAsync(topic, new RedisTestMessage { Id = 3 }, new PublishOptions { RoutingKey = "shipping.us.created" });

        await twoMessages.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, received.ToArray());
    }

    [TestMethod]
    public async Task SubscribeAsync_SamePatternOnDifferentTopics_IsolatesLogicalTopics()
    {
        var topicA = $"topic-a-{Guid.NewGuid():N}";
        var topicB = $"topic-b-{Guid.NewGuid():N}";
        var topicAReceived = new ConcurrentBag<int>();
        var topicBReceived = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var topicAReceivedOwnMessage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new SubscribeOptions { RoutingPattern = "orders.*.created" };

        await using var subscriptionA = await _queue.SubscribeAsync<RedisTestMessage>(
            topicA,
            context =>
            {
                topicAReceived.Add(context.Envelope.Payload.Id);
                if (context.Envelope.Payload.Id == 1)
                {
                    topicAReceivedOwnMessage.TrySetResult();
                }

                return Task.FromResult(MessageResult.Success());
            },
            options);
        await using var subscriptionB = await _queue.SubscribeAsync<RedisTestMessage>(
            topicB,
            context =>
            {
                topicBReceived.TrySetResult(context.Envelope.Payload.Id);
                return Task.FromResult(MessageResult.Success());
            },
            options);

        await _queue.PublishAsync(
            topicB,
            new RedisTestMessage { Id = 2 },
            new PublishOptions { RoutingKey = "orders.us.created" });

        Assert.AreEqual(2, await topicBReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(200);
        Assert.IsFalse(topicAReceived.Contains(2), "Topic A must not receive Topic B traffic.");

        await _queue.PublishAsync(
            topicA,
            new RedisTestMessage { Id = 1 },
            new PublishOptions { RoutingKey = "orders.eu.created" });
        await topicAReceivedOwnMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.Contains(topicAReceived.ToArray(), 1);
    }

    [TestMethod]
    public async Task PublishBatchAsync_RoutingKeyConfigured_DeliversOnRoutingChannel()
    {
        var topic = $"batch-{Guid.NewGuid():N}";
        var received = new ConcurrentBag<int>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await _queue.SubscribeAsync<RedisTestMessage>(
            topic,
            context =>
            {
                received.Add(context.Envelope.Payload.Id);
                if (received.Count == 2)
                {
                    completed.TrySetResult();
                }

                return Task.FromResult(MessageResult.Success());
            },
            new SubscribeOptions { RoutingPattern = "orders.*.updated" });

        await _queue.PublishBatchAsync(
            topic,
            new[] { new RedisTestMessage { Id = 1 }, new RedisTestMessage { Id = 2 } },
            new PublishOptions { RoutingKey = "orders.eu.updated" });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, received.ToArray());
    }

    [TestMethod]
    public async Task SubscribeAsync_ParentTokenCanceledAfterSetup_CancelsActiveHandler()
    {
        var topic = $"lifetime-{Guid.NewGuid():N}";
        using var lifetimeTokenSource = new CancellationTokenSource();
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var subscription = await _queue.SubscribeAsync<RedisTestMessage>(
            topic,
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
            cancellationToken: lifetimeTokenSource.Token);

        await _queue.PublishAsync(topic, new RedisTestMessage { Id = 1 });
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await lifetimeTokenSource.CancelAsync();

        await handlerCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertEventuallyAsync(() => !subscription.IsActive, TimeSpan.FromSeconds(5));
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
}

public sealed class RedisTestMessage
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;
}
