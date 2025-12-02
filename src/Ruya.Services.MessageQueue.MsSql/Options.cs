using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// Configuration options for SQL Server Service Broker message bus
/// </summary>
public sealed class MsSqlOptions
{
    /// <summary>
    /// SQL Server connection string
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Wait timeout in milliseconds for WAITFOR(RECEIVE) (default: 1000ms)
    /// Set to 0 for non-blocking RECEIVE
    /// </summary>
    public int ReceiveTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Batch size for receiving messages (default: 10)
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
    /// Enable conversation pooling to reuse conversations (default: false)
    /// Set to true for high-throughput scenarios to avoid conversation overhead
    /// </summary>
    public bool EnableConversationPooling { get; set; } = false;

    /// <summary>
    /// Maximum number of pooled conversations per topic (default: 10)
    /// Only used when EnableConversationPooling = true
    /// </summary>
    public int MaxPooledConversations { get; set; } = 10;
}
