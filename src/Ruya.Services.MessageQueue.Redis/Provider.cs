using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Telemetry;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Redis implementation of IMessageQueueProvider
/// Supports both Pub/Sub and Streams
/// </summary>
public sealed class RedisProvider : IMessageQueueProvider
{
    private static readonly EventId CreatingEvent = new(3201, "RedisProviderCreating");
    private readonly IOptions<RedisOptions> _options;
    private readonly IMessageSerializer _serializer;
    private readonly IEnumerable<IMessageMiddleware> _middlewares;
    private readonly MessageQueueTelemetry _telemetry;
    private readonly ILogger<RedisProvider> _logger;

    [Obsolete("Resolve RedisProvider from dependency injection or use the constructor that accepts MessageQueueTelemetry. This constructor will be removed in version 9.0.")]
    public RedisProvider(
        IOptions<RedisOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger<RedisProvider> logger)
        : this(
            options,
            serializer,
            middlewares,
            new MessageQueueTelemetry(Options.Create(new MessageQueueOptions())),
            logger)
    {
    }

    public RedisProvider(
        IOptions<RedisOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        MessageQueueTelemetry telemetry,
        ILogger<RedisProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => nameof(Ruya.Services.MessageQueue.Redis);

    public ProviderCapabilities Capabilities => new()
    {
        SupportsPriority = false, // Redis doesn't have native priority
        SupportsDelayedDelivery = false, // Can be emulated with sorted sets
        SupportsTimeToLive = false,
        SupportsPublisherConfirms = false,
        SupportsConsumerGroups = false, // Subscription currently uses Pub/Sub only
        SupportsDeadLetterQueue = false, // Can be emulated
        SupportsReplay = false, // Streams can be published, but stream consumption is not implemented
        SupportsBatchPublish = true,
        SupportsTransactions = false, // Redis Pub/Sub doesn't support transactions
        MaxPriorityLevel = null // No native priority support
    };

    public Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation(CreatingEvent, "Creating Redis message queue instance: {Name}", name);

        IMessageQueue queue = new RedisMessageQueue(
            name,
            _options,
            _serializer,
            _middlewares,
            _telemetry,
            _logger);

        return Task.FromResult(queue);
    }
}
