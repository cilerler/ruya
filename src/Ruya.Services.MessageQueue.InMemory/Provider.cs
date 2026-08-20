using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Telemetry;


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

    public string ProviderName => nameof(Ruya.Services.MessageQueue.InMemory);

    public ProviderCapabilities Capabilities => new()
    {
        SupportsPriority = false,
        SupportsDelayedDelivery = true,
        SupportsTimeToLive = true,
        SupportsPublisherConfirms = false,
        SupportsDeadLetterQueue = true,
        SupportsReplay = false,
        SupportsBatchPublish = true,
        SupportsTransactions = false, // In-memory is not durable
        SupportsConsumerGroups = true,
        MaxPriorityLevel = null
    };

    public Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation(InMemoryLogEvents.Provider, "Creating InMemory message bus instance: {Name}", name);

        var options = _serviceProvider.GetRequiredService<IOptions<InMemoryOptions>>();
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
        var middlewares = _serviceProvider.GetServices<IMessageMiddleware>();
        var telemetry = _serviceProvider.GetRequiredService<MessageQueueTelemetry>();
        var deadLetterStore = _serviceProvider.GetRequiredService<IInMemoryDeadLetterStore>();
        var logger = _serviceProvider.GetRequiredService<ILogger<InMemoryMessageQueue>>();

        var queue = new InMemoryMessageQueue(
            name,
            options,
            serializer,
            middlewares,
            telemetry,
            deadLetterStore,
            logger);

        _logger.LogInformation(InMemoryLogEvents.Provider, "InMemory message queue '{Name}' created successfully", name);

        return Task.FromResult<IMessageQueue>(queue);
    }
}
