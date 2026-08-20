using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Telemetry;


namespace Ruya.Services.MessageQueue.RabbitMq;

/// <summary>
/// RabbitMQ implementation of IMessageQueueProvider
/// </summary>
public sealed class RabbitMQProvider : IMessageQueueProvider
{
    private static readonly EventId QueueCreating = new(1000, nameof(QueueCreating));
    private static readonly EventId ConnectionOpening = new(1001, nameof(ConnectionOpening));
    private static readonly EventId ConnectionOpened = new(1002, nameof(ConnectionOpened));

    private readonly IOptions<RabbitMQOptions> _options;
    private readonly IMessageSerializer _serializer;
    private readonly IEnumerable<IMessageMiddleware> _middlewares;
    private readonly MessageQueueTelemetry _telemetry;
    private readonly ILogger<RabbitMQProvider> _logger;

    /// <summary>
    /// Creates the provider using the former 8.x constructor shape.
    /// </summary>
    [Obsolete("Resolve RabbitMQProvider from dependency injection or use the constructor that accepts MessageQueueTelemetry. This constructor will be removed in version 9.0.")]
    public RabbitMQProvider(
        IOptions<RabbitMQOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger<RabbitMQProvider> logger)
        : this(
            options,
            serializer,
            middlewares,
            new MessageQueueTelemetry(
                Microsoft.Extensions.Options.Options.Create(new MessageQueueOptions())),
            logger)
    {
    }

    public RabbitMQProvider(
        IOptions<RabbitMQOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        MessageQueueTelemetry telemetry,
        ILogger<RabbitMQProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
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
        SupportsReplay = false,
        SupportsBatchPublish = true,
        SupportsTransactions = false, // This implementation uses publisher confirms, not AMQP transactions
        MaxPriorityLevel = 255 // Byte-based priority (0-255)
    };

    public async Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(QueueCreating, "Creating RabbitMQ message bus instance: {Name}", name);

        var options = _options.Value;

        var factory = CreateConnectionFactory(options);

        // Create connection asynchronously (NO blocking!)
        _logger.LogDebug(
            ConnectionOpening,
            "Establishing RabbitMQ connection for bus '{Name}' to {Host}:{Port}",
            name,
            options.Host,
            options.Port);
        var connection = await factory.CreateConnectionAsync(cancellationToken);
        _logger.LogInformation(ConnectionOpened, "RabbitMQ connection established for bus '{Name}'", name);

        // Create queue with the established connection
        IMessageQueue queue = new RabbitMQMessageQueue(
            name,
            connection,
            options,
            _serializer,
            _middlewares,
            _telemetry,
            _logger);

        return queue;
    }

    internal static ConnectionFactory CreateConnectionFactory(RabbitMQOptions options)
    {
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
            RequestedConnectionTimeout = options.ConnectionTimeout
        };

        if (options.UseSsl)
        {
            factory.Ssl = new SslOption(options.Host, enabled: true);
        }

        return factory;
    }
}
