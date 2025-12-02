using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;

namespace Ruya.Services.MessageQueue.Redis;

/// <summary>
/// Redis implementation of IMessageQueueProvider
/// Supports both Pub/Sub and Streams
/// </summary>
public sealed class RedisProvider : IMessageQueueProvider
{
    private readonly IOptions<RedisOptions> _options;
    private readonly IMessageSerializer _serializer;
    private readonly IEnumerable<IMessageMiddleware> _middlewares;
    private readonly ILogger<RedisProvider> _logger;

    public RedisProvider(
        IOptions<RedisOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger<RedisProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "Redis";

    public ProviderCapabilities Capabilities => new()
    {
        SupportsPriority = false, // Redis doesn't have native priority
        SupportsDelayedDelivery = false, // Can be emulated with sorted sets
        SupportsTimeToLive = true,
        SupportsPublisherConfirms = false,
        SupportsConsumerGroups = true, // Via streams
        SupportsDeadLetterQueue = false, // Can be emulated
        SupportsReplay = true, // Via streams
        SupportsBatchPublish = true,
        SupportsTransactions = false, // Redis Pub/Sub doesn't support transactions
        MaxPriorityLevel = null // No native priority support
    };

    public Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating Redis message queue instance: {Name}", name);

        IMessageQueue queue = new RedisMessageQueue(
            name,
            _options,
            _serializer,
            _middlewares,
            _logger);

        return Task.FromResult(queue);
    }
}
