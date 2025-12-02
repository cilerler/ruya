using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Serialization;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// SQL Server Service Broker implementation of IMessageQueueProvider
/// </summary>
public sealed class MsSqlProvider : IMessageQueueProvider
{
    private readonly IOptions<MsSqlOptions> _options;
    private readonly IMessageSerializer _serializer;
    private readonly IEnumerable<IMessageMiddleware> _middlewares;
    private readonly ILogger<MsSqlProvider> _logger;

    public MsSqlProvider(
        IOptions<MsSqlOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger<MsSqlProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "MsSql";

    public ProviderCapabilities Capabilities => new()
    {
        SupportsPriority = false,         // Service Broker has priorities but managed differently
        SupportsDelayedDelivery = false,  // Would require custom implementation
        SupportsTimeToLive = false,       // Would require custom implementation
        SupportsPublisherConfirms = true, // Transaction-based
        SupportsConsumerGroups = false,   // Competing consumers share same queue
        SupportsDeadLetterQueue = true,   // Custom DLQ table
        SupportsReplay = false,           // Messages are removed after processing
        SupportsBatchPublish = true,
        SupportsTransactions = true,      // Service Broker is fully transactional
        MaxPriorityLevel = 10             // Service Broker priority levels (1-10)
    };

    public async Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating SQL Server Service Broker message bus instance: {Name}", name);

        var options = _options.Value;

        // Validate connection and Service Broker status
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Check if Service Broker is enabled
        await EnsureServiceBrokerEnabledAsync(connection, options, cancellationToken);

        // Auto-create schema if enabled
        if (options.AutoCreateSchema)
        {
            await EnsureSchemaExistsAsync(connection, options, cancellationToken);
        }

        _logger.LogInformation("SQL Server Service Broker message queue '{Name}' created successfully", name);

        // Create queue instance
        IMessageQueue queue = new MsSqlMessageQueue(
            name,
            options,
            _serializer,
            _middlewares,
            _logger);

        return queue;
    }

    private async Task EnsureServiceBrokerEnabledAsync(
        SqlConnection connection,
        MsSqlOptions options,
        CancellationToken cancellationToken)
    {
        var checkSql = "SELECT is_broker_enabled FROM sys.databases WHERE name = DB_NAME()";
        await using var checkCmd = new SqlCommand(checkSql, connection);
        var isEnabled = (bool)(await checkCmd.ExecuteScalarAsync(cancellationToken))!;

        if (!isEnabled)
        {
            if (options.AutoEnableServiceBroker)
            {
                _logger.LogWarning("Service Broker is not enabled. Attempting to enable it (requires ALTER DATABASE permission)");

                try
                {
                    // Use QUOTENAME to safely escape database name and prevent SQL injection
                    var dbName = connection.Database;
                    var enableSql = $"ALTER DATABASE {QuoteName(dbName)} SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE";
                    await using var enableCmd = new SqlCommand(enableSql, connection);
                    await enableCmd.ExecuteNonQueryAsync(cancellationToken);

                    _logger.LogInformation("Service Broker enabled successfully on database '{DatabaseName}'", dbName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enable Service Broker. Please enable it manually.");
                    throw new InvalidOperationException(
                        $"Service Broker is not enabled on database '{connection.Database}'. " +
                        $"Please run: ALTER DATABASE {QuoteName(connection.Database)} SET ENABLE_BROKER", ex);
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Service Broker is not enabled on database '{connection.Database}'. " +
                    $"Please enable it: ALTER DATABASE {QuoteName(connection.Database)} SET ENABLE_BROKER");
            }
        }
        else
        {
            _logger.LogDebug("Service Broker is enabled");
        }
    }

    private async Task EnsureSchemaExistsAsync(
        SqlConnection connection,
        MsSqlOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Ensuring Service Broker schema exists");

        // Check if message type exists
        var checkSql = @"
            SELECT COUNT(*)
            FROM sys.service_message_types
            WHERE name = 'RuyaServicesMessageQueueMessage'";

        await using var checkCmd = new SqlCommand(checkSql, connection);
        var schemaExists = (int)(await checkCmd.ExecuteScalarAsync(cancellationToken))! > 0;

        if (!schemaExists)
        {
            _logger.LogInformation("Creating Service Broker schema (message types, contracts, stored procedures)");

            var schemaScript = GetServiceBrokerSchemaScript();

            // Split by GO statements and execute each batch
            var batches = schemaScript.Split(new[] { "\r\nGO\r\n", "\nGO\n", "\r\nGO\n", "\nGO\r\n" },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;

                await using var batchCmd = new SqlCommand(batch, connection);
                batchCmd.CommandTimeout = options.CommandTimeoutSeconds;
                await batchCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("Service Broker schema created successfully");
        }
    }

    private string GetServiceBrokerSchemaScript()
    {
        // Embedded schema from ServiceBrokerSchema.sql
        return @"
-- Create message type for our bus messages
IF NOT EXISTS (SELECT * FROM sys.service_message_types WHERE name = 'RuyaServicesMessageQueueMessage')
BEGIN
    CREATE MESSAGE TYPE [RuyaServicesMessageQueueMessage] VALIDATION = NONE;
END

-- Create contract for message exchange
IF NOT EXISTS (SELECT * FROM sys.service_contracts WHERE name = 'RuyaServicesMessageQueueContract')
BEGIN
    CREATE CONTRACT [RuyaServicesMessageQueueContract]
    (
        [RuyaServicesMessageQueueMessage] SENT BY ANY
    );
END

-- Create stored procedure to create a queue and service for a topic
IF OBJECT_ID('dbo.RuyaServicesMessageQueue_CreateTopicService', 'P') IS NOT NULL
    DROP PROCEDURE [dbo].[RuyaServicesMessageQueue_CreateTopicService];

EXEC('
CREATE PROCEDURE [dbo].[RuyaServicesMessageQueue_CreateTopicService]
    @TopicName NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @QueueName NVARCHAR(300) = ''RuyaServicesMessageQueueQueue_'' + REPLACE(@TopicName, ''.'', ''_'');
    DECLARE @ServiceName NVARCHAR(300) = ''RuyaServicesMessageQueueService_'' + REPLACE(@TopicName, ''.'', ''_'');
    DECLARE @SQL NVARCHAR(MAX);

    IF NOT EXISTS (SELECT * FROM sys.service_queues WHERE name = @QueueName)
    BEGIN
        SET @SQL = N''CREATE QUEUE ['' + @QueueName + ''] WITH STATUS = ON, RETENTION = OFF;'';
        EXEC sp_executesql @SQL;
    END

    IF NOT EXISTS (SELECT * FROM sys.services WHERE name = @ServiceName)
    BEGIN
        SET @SQL = N''CREATE SERVICE ['' + @ServiceName + ''] ON QUEUE ['' + @QueueName + ''] ([RuyaServicesMessageQueueContract]);'';
        EXEC sp_executesql @SQL;
    END
END
');

-- Create dead letter table
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'RuyaServicesMessageQueueDeadLetter')
BEGIN
    CREATE TABLE [dbo].[RuyaServicesMessageQueueDeadLetter]
    (
        [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [MessageId] UNIQUEIDENTIFIER NOT NULL,
        [TopicName] NVARCHAR(255) NOT NULL,
        [MessagePayload] NVARCHAR(MAX) NOT NULL,
        [ErrorMessage] NVARCHAR(MAX) NULL,
        [DeliveryAttempts] INT NOT NULL,
        [OriginalTimestamp] DATETIME2 NOT NULL,
        [DeadLetterTimestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        INDEX IX_RuyaServicesMessageQueueDeadLetter_TopicName ([TopicName]),
        INDEX IX_RuyaServicesMessageQueueDeadLetter_Timestamp ([DeadLetterTimestamp])
    );
END
";
    }

    /// <summary>
    /// Safely quotes a SQL Server identifier to prevent SQL injection
    /// </summary>
    private static string QuoteName(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null or whitespace", nameof(identifier));

        // QUOTENAME in C# - escape ] with ]]
        return "[" + identifier.Replace("]", "]]") + "]";
    }
}
