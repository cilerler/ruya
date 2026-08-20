using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Telemetry;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// SQL Server Service Broker implementation of IMessageQueueProvider
/// </summary>
public sealed class MsSqlProvider : IMessageQueueProvider
{
    private static readonly EventId CreatingEvent = new(4001, "MsSqlProviderCreating");
    private static readonly EventId CreatedEvent = new(4002, "MsSqlProviderCreated");
    private static readonly EventId BrokerEnableAttemptEvent = new(4003, "MsSqlBrokerEnableAttempt");
    private static readonly EventId BrokerEnabledEvent = new(4004, "MsSqlBrokerEnabled");
    private static readonly EventId BrokerEnableFailedEvent = new(4005, "MsSqlBrokerEnableFailed");
    private static readonly EventId BrokerAvailableEvent = new(4006, "MsSqlBrokerAvailable");
    private static readonly EventId SchemaCheckEvent = new(4007, "MsSqlSchemaCheck");
    private static readonly EventId SchemaCurrentEvent = new(4008, "MsSqlSchemaCurrent");
    private readonly IOptions<MsSqlOptions> _options;
    private readonly IMessageSerializer _serializer;
    private readonly IEnumerable<IMessageMiddleware> _middlewares;
    private readonly MessageQueueTelemetry _telemetry;
    private readonly ILogger<MsSqlProvider> _logger;

    [Obsolete("Resolve MsSqlProvider from dependency injection or use the constructor that accepts MessageQueueTelemetry. This constructor will be removed in version 9.0.")]
    public MsSqlProvider(
        IOptions<MsSqlOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger<MsSqlProvider> logger)
        : this(
            options,
            serializer,
            middlewares,
            new MessageQueueTelemetry(Options.Create(new MessageQueueOptions())),
            logger)
    {
    }

    public MsSqlProvider(
        IOptions<MsSqlOptions> options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        MessageQueueTelemetry telemetry,
        ILogger<MsSqlProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _middlewares = middlewares ?? throw new ArgumentNullException(nameof(middlewares));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => nameof(Ruya.Services.MessageQueue.MsSql);

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
        MaxPriorityLevel = null
    };

    public async Task<IMessageQueue> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(CreatingEvent, "Creating SQL Server Service Broker message bus instance: {Name}", name);

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

        _logger.LogInformation(CreatedEvent, "SQL Server Service Broker message queue '{Name}' created successfully", name);

        // Create queue instance
        IMessageQueue queue = new MsSqlMessageQueue(
            name,
            options,
            _serializer,
            _middlewares,
            _telemetry,
            _logger);

        return queue;
    }

    private async Task EnsureServiceBrokerEnabledAsync(
        SqlConnection connection,
        MsSqlOptions options,
        CancellationToken cancellationToken)
    {
        await using var checkCmd = new SqlCommand(SqlResources.CheckServiceBrokerEnabled, connection);
        checkCmd.CommandTimeout = options.CommandTimeoutSeconds;
        checkCmd.Parameters.Add("@p0", System.Data.SqlDbType.Bit).Value = false;
        var isEnabled = (bool)(await checkCmd.ExecuteScalarAsync(cancellationToken))!;

        if (!isEnabled)
        {
            if (options.AutoEnableServiceBroker)
            {
                _logger.LogWarning(BrokerEnableAttemptEvent, "Service Broker is not enabled. Attempting to enable it (requires ALTER DATABASE permission)");

                try
                {
                    var dbName = connection.Database;
                    var masterConnectionString = new SqlConnectionStringBuilder(options.ConnectionString)
                    {
                        InitialCatalog = "master",
                    }.ConnectionString;
                    await using var masterConnection = new SqlConnection(masterConnectionString);
                    await masterConnection.OpenAsync(cancellationToken);
                    await using var enableCmd = new SqlCommand(SqlResources.EnableServiceBroker, masterConnection);
                    enableCmd.CommandTimeout = options.CommandTimeoutSeconds;
                    enableCmd.Parameters.Add("@p0", System.Data.SqlDbType.NVarChar, 128).Value = dbName;
                    enableCmd.Parameters.Add("@p1", System.Data.SqlDbType.Bit).Value = false;
                    await enableCmd.ExecuteNonQueryAsync(cancellationToken);

                    // WITH ROLLBACK IMMEDIATE terminates every target-database session, including
                    // this provider's initial connection. Reopen it before schema provisioning.
                    await connection.CloseAsync();
                    await connection.OpenAsync(cancellationToken);

                    _logger.LogInformation(BrokerEnabledEvent, "Service Broker enabled successfully on database '{DatabaseName}'", dbName);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(BrokerEnableFailedEvent, ex, "Failed to enable Service Broker. Please enable it manually.");
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
            _logger.LogDebug(BrokerAvailableEvent, "Service Broker is enabled");
        }
    }

    private async Task EnsureSchemaExistsAsync(
        SqlConnection connection,
        MsSqlOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(SchemaCheckEvent, "Ensuring Service Broker schema exists");

        // The script is deliberately idempotent and always runs. Besides first-time creation it
        // upgrades existing queues and DLQ columns from earlier package versions.
        var batches = SplitSchemaBatches(SqlResources.ServiceBrokerSchema);

        foreach (var batch in batches)
        {
            await using var batchCmd = CreateSchemaBatchCommand(
                batch,
                connection,
                options.CommandTimeoutSeconds);
            await batchCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogDebug(SchemaCurrentEvent, "Service Broker schema is current");
    }

    private static List<string> SplitSchemaBatches(string schemaScript)
    {
        ArgumentNullException.ThrowIfNull(schemaScript);

        var batches = new List<string>();
        var batchLines = new List<string>();
        using var reader = new StringReader(schemaScript);
        while (reader.ReadLine() is { } line)
        {
            if (string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))
            {
                AddCurrentBatch();
                continue;
            }

            batchLines.Add(line);
        }

        AddCurrentBatch();
        return batches;

        void AddCurrentBatch()
        {
            var batch = string.Join(Environment.NewLine, batchLines).Trim();
            batchLines.Clear();
            if (!string.IsNullOrWhiteSpace(batch))
            {
                batches.Add(batch);
            }
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Command text is split only from the package-owned embedded Service Broker schema resource.")]
    private static SqlCommand CreateSchemaBatchCommand(
        string batch,
        SqlConnection connection,
        int commandTimeoutSeconds)
    {
        var command = new SqlCommand(batch, connection)
        {
            CommandTimeout = commandTimeoutSeconds,
        };

        // Only the first schema batch declares the embedded-resource debug contract. Supplying an
        // unused sp_executesql parameter to a CREATE OR ALTER PROCEDURE batch prevents that DDL from
        // being the first statement in the batch.
        if (batch.Contains("DECLARE @Debug BIT = COALESCE(@p0", StringComparison.Ordinal))
        {
            command.Parameters.Add("@p0", System.Data.SqlDbType.Bit).Value = false;
        }

        return command;
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
