using Microsoft.Extensions.Logging;

namespace Ruya.Services.MessageQueue.InMemory;

internal static class InMemoryLogEvents
{
    internal static readonly EventId QueueLifecycle = new(2001, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(QueueLifecycle)}");
    internal static readonly EventId Publish = new(2002, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(Publish)}");
    internal static readonly EventId Subscription = new(2003, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(Subscription)}");
    internal static readonly EventId Topology = new(2004, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(Topology)}");
    internal static readonly EventId DelayedDelivery = new(2005, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(DelayedDelivery)}");
    internal static readonly EventId Processing = new(2101, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(Processing)}");
    internal static readonly EventId Retry = new(2102, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(Retry)}");
    internal static readonly EventId DeadLetter = new(2103, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(DeadLetter)}");
    internal static readonly EventId Disposal = new(2104, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(Disposal)}");
    internal static readonly EventId Provider = new(2201, $"{nameof(Ruya.Services.MessageQueue.InMemory)}{nameof(Provider)}");
}
