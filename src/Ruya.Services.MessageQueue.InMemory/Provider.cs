using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;


namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// Provider for creating in-memory message queue instances
/// </summary>
public sealed class InMemoryProvider : IMessageQueueProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InMemoryProvider> _logger;

    public InMemoryProvider(IServiceProvider serviceProvider, ILogger<InMemoryProvider> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "InMemory";

    public ProviderCapabilities Capabilities => new()
    {
        SupportsPriority = true,
        SupportsDelayedDelivery = true,
        SupportsDeadLetterQueue = true,
        SupportsReplay = true, // When message store is enabled
        SupportsTransactions = false, // In-memory is not durable
        SupportsConsumerGroups = true,
        MaxPriorityLevel = 255
    };

    public Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating InMemory message bus instance: {Name}", name);

        var options = _serviceProvider.GetRequiredService<IOptions<InMemoryOptions>>();
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
        var middlewares = _serviceProvider.GetServices<IMessageMiddleware>();
        var logger = _serviceProvider.GetRequiredService<ILogger<InMemoryMessageQueue>>();

        var queue = new InMemoryMessageQueue(
            name,
            options,
            serializer,
            middlewares,
            logger);

        _logger.LogInformation("InMemory message queue '{Name}' created successfully", name);

        return Task.FromResult<IMessageQueue>(queue);
    }
}
