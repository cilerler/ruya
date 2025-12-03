using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;


namespace Ruya.Services.MessageQueue.RabbitMq;

/// <summary>
/// RabbitMQ implementation of IMessageQueueProvider
/// </summary>
public sealed class RabbitMQProvider : IMessageQueueProvider
{
    private readonly IOptions<RabbitMQOptions> _options;
    private readonly IMessageSerializer _serializer;
    private readonly IEnumerable<IMessageMiddleware> _middlewares;
    private readonly ILogger<RabbitMQProvider> _logger;

    public RabbitMQProvider(
        IOptions<RabbitMQOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger<RabbitMQProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "RabbitMQ";

    public ProviderCapabilities Capabilities => new()
    {
        SupportsPriority = true,
        SupportsDelayedDelivery = true, // via plugin or DLX
        SupportsTimeToLive = true,
        SupportsPublisherConfirms = true,
        SupportsConsumerGroups = true, // via competing consumers
        SupportsDeadLetterQueue = true,
        SupportsReplay = true, // via streams
        SupportsBatchPublish = true,
        SupportsTransactions = true, // RabbitMQ supports transactions
        MaxPriorityLevel = 255 // Byte-based priority (0-255)
    };

    public async Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating RabbitMQ message bus instance: {Name}", name);

        var options = _options.Value;

        // Create connection factory
        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            VirtualHost = options.VirtualHost,
            UserName = options.Username,
            Password = options.Password,
            AutomaticRecoveryEnabled = options.AutomaticRecoveryEnabled,
            NetworkRecoveryInterval = options.NetworkRecoveryInterval,
            RequestedHeartbeat = options.Heartbeat,
            // DispatchConsumersAsync is removed in v7

        };

        // Create connection asynchronously (NO blocking!)
        _logger.LogDebug("Establishing RabbitMQ connection for bus '{Name}' to {Host}:{Port}", name, options.Host, options.Port);
        var connection = await factory.CreateConnectionAsync(cancellationToken);
        _logger.LogInformation("RabbitMQ connection established for bus '{Name}'", name);

        // Create queue with the established connection
        IMessageQueue queue = new RabbitMQMessageQueue(
            name,
            connection,
            options,
            _serializer,
            _middlewares,
            _logger);

        return queue;
    }
}
