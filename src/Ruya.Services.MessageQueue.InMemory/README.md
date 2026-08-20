# Ruya.Services.MessageQueue.InMemory

In-Memory provider implementation for `Ruya.Services.MessageQueue`. Ideal for testing, local development, and single-process applications.

## Features

-   **Zero Dependencies**: No external broker required.
-   **Fast**: Direct memory references (or serialized copies).
-   **Testing**: Perfect for unit and integration tests.
-   **Channels**: Uses `System.Threading.Channels` for async coordination.

The provider preserves enqueue order and does not implement priority delivery. The released
`EnablePriority` property remains as an obsolete compatibility bridge, while provider capabilities
report priority as unsupported.

## Configuration

Calling `AddInMemoryProvider()` binds `MessageQueue:InMemory` and validates the resulting options at
host startup. The typed callback remains available for code-based configuration and overrides bound
values.

```csharp
services.AddMessageQueue(options =>
{
    options.DefaultProvider = "memory";
    options.Providers["memory"] = new ProviderConfiguration
    {
        Type = "InMemory",
        Enabled = true
    };
})
.AddInMemoryProvider(options =>
{
    // Compatibility defaults used when a subscription supplies no retry policy or delivery cap.
    options.MaxRetryAttempts = 3; // Includes the initial delivery.
    options.RetryDelay = TimeSpan.FromSeconds(1);
    options.DeadLetterQueueCapacity = 1000; // Oldest entry is discarded when full.
});
```

## Delivery policy

Retry behavior is provider-neutral at subscription time:

- `MaxDeliveryCount` is the finite, authoritative delivery cap when specified.
- When only `RetryPolicy` is specified, the cap is one initial delivery plus
  `RetryPolicy.MaxRetryAttempts`.
- When neither is specified, `InMemoryOptions.MaxRetryAttempts` and `RetryDelay` preserve the
  provider's compatibility defaults.
- `RetryPolicy` controls fixed or exponential delay and optional bounded jitter. When the cap is
  reached, a handler `Retry` result is applied as `Reject` and sent to the in-memory DLQ when it is
  enabled.
- Handler, middleware, and deserialization exceptions are not retried by default. Set
  `RequeueOnException = true` only for known-transient failures; the same finite cap applies.
- Host-requested cancellation is incomplete rather than poison: the provider emits no completed
  delivery outcome and returns the message through an internal redelivery path that does not compete
  with bounded publisher capacity. A later subscription to the same explicit consumer group remains
  eligible to receive every unfinished message.

```csharp
var subscription = await queue.SubscribeAsync<OrderEvent>(
    "orders",
    HandleOrderAsync,
    new SubscribeOptions
    {
        ConsumerGroup = "order-workers",
        MaxDeliveryCount = 4,
        RequeueOnException = true,
        RetryPolicy = new RetryPolicy
        {
            MaxRetryAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(200),
            MaxDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2,
            UseExponentialBackoff = true,
            UseJitter = true
        }
    });
```

## Dead-letter inspection

When the DLQ is enabled, terminal failures are retained in a bounded provider-specific store. Resolve
`IInMemoryDeadLetterStore` and use the named queue passed to `CreateQueueAsync`:

```csharp
var deadLetters = serviceProvider.GetRequiredService<IInMemoryDeadLetterStore>();
var snapshot = deadLetters.GetSnapshot("memory");

if (deadLetters.TryDequeue("memory", out var oldest))
{
    // Inspect or explicitly replay oldest.SerializedMessage.
}
```

Snapshots are oldest-to-newest. Retention is process-local, and reaching
`DeadLetterQueueCapacity` discards the oldest retained entry.

## Usage

### Testing Scenarios

```csharp
[TestClass]
public class OrderServiceTests
{
    [TestMethod]
    public async Task CreateOrder_OrderAccepted_PublishesEvent()
    {
        // Setup
        var services = new ServiceCollection();
        services.AddMessageQueue(options =>
        {
            options.Providers["memory"] = new ProviderConfiguration
            {
                Type = "InMemory",
                Enabled = true
            };
        }).AddInMemoryProvider();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IMessageQueueFactory>();
        var queue = await factory.CreateQueueAsync("memory");

        // Subscribe
        var received = new TaskCompletionSource<OrderEvent>();
        await queue.SubscribeAsync<OrderEvent>("orders", async ctx => 
        {
            received.SetResult(ctx.Envelope.Payload);
            return MessageResult.Success();
        });

        // Act
        await queue.PublishAsync("orders", new OrderEvent { Id = 1 });

        // Assert
        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, result.Id);
    }
}
```
