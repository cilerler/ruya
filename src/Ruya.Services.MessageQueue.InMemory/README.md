# Ruya.Services.MessageQueue.InMemory

In-Memory provider implementation for `Ruya.Services.MessageQueue`. Ideal for testing, local development, and single-process applications.

## Features

-   **Zero Dependencies**: No external broker required.
-   **Fast**: Direct memory references (or serialized copies).
-   **Testing**: Perfect for unit and integration tests.
-   **Channels**: Uses `System.Threading.Channels` for async coordination.

## Configuration

```csharp
services.AddMessageQueue(options =>
{
    options.DefaultProvider = "memory";
})
.AddInMemory(options =>
{
    // Optional: Simulate serialization to catch serialization issues during testing
    options.SimulateSerialization = true; 
});
```

## Usage

### Testing Scenarios

```csharp
public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrder_PublishesEvent()
    {
        // Setup
        var services = new ServiceCollection();
        services.AddMessageQueue().AddInMemory();
        var sp = services.BuildServiceProvider();
        var queue = sp.GetRequiredService<IMessageQueue>();

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
        Assert.Equal(1, result.Id);
    }
}
```
