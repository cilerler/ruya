using System;
using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// Configuration options for SQL Server Service Broker message bus
/// </summary>
public sealed class MsSqlOptions
{
    public const string ConfigurationSectionName =
        $"{nameof(Ruya.Services.MessageQueue)}:{nameof(Ruya.Services.MessageQueue.MsSql)}";

    /// <summary>
    /// Descriptive key used to resolve the Service Broker connection from the top-level
    /// <c>ConnectionStrings</c> catalog.
    /// </summary>
    public string? MessageQueueConnectionStringKey { get; set; }

    /// <summary>
    /// Resolved SQL Server connection string. Retained for released 8.x typed-configuration
    /// compatibility; standard configuration should set
    /// <see cref="MessageQueueConnectionStringKey"/> instead.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    internal bool ConnectionStringResolvedFromCatalog { get; set; }

    /// <summary>
    /// Wait timeout in milliseconds for WAITFOR(RECEIVE) (default: 1000ms)
    /// Set to 0 for non-blocking RECEIVE
    /// </summary>
    public int ReceiveTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Reserved receive batch hint (default: 10). The transactional subscriber currently receives
    /// one message per SQL transaction so retry and host cancellation can preserve that delivery.
    /// Use <see cref="Abstractions.SubscribeOptions.MaxConcurrency"/> for parallel handlers.
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Maximum delivery attempts before moving to dead letter (default: 5)
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>
    /// Enable automatic schema creation on startup (default: true)
    /// Creates message types, contracts, queues, services, and stored procedures
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>
    /// Enable Service Broker on database automatically (default: false)
    /// Requires ALTER DATABASE permission - should be done manually in production
    /// </summary>
    public bool AutoEnableServiceBroker { get; set; } = false;

    /// <summary>
    /// Command timeout in seconds (default: 30s)
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Polling interval in milliseconds between receive attempts (default: 100ms)
    /// Used for continuous message processing
    /// </summary>
    public int PollingIntervalMs { get; set; } = 100;

    /// <summary>
    /// Reserved for a future conversation-pooling implementation. The current provider rejects
    /// <see langword="true"/> during startup validation instead of silently ignoring it.
    /// </summary>
    [Obsolete("Conversation pooling is not supported by the Service Broker provider. Leave this disabled. This property will be removed in version 9.0.")]
    public bool EnableConversationPooling { get; set; } = false;

    /// <summary>
    /// Reserved maximum number of pooled conversations per topic (default: 10).
    /// </summary>
    [Obsolete("Conversation pooling is not supported by the Service Broker provider. This property will be removed in version 9.0.")]
    public int MaxPooledConversations { get; set; } = 10;
}
