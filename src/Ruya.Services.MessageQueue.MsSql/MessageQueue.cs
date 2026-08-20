using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Telemetry;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// SQL Server Service Broker implementation of IMessageQueue
/// </summary>
internal sealed class MsSqlMessageQueue : IMessageQueue
{
    private const string DeliveryCountHeader = "ruya.message_queue.delivery_count";
    private static readonly EventId SubscriptionCreatingEvent = new(4101, "MsSqlSubscriptionCreating");
    private static readonly EventId SubscriptionCreatedEvent = new(4102, "MsSqlSubscriptionCreated");
    private static readonly EventId HealthCheckFailedEvent = new(4103, "MsSqlHealthCheckFailed");
    private static readonly EventId DisposingEvent = new(4104, "MsSqlDisposing");
    private static readonly EventId PublishCommittedEvent = new(4105, "MsSqlPublishCommitted");
    private static readonly EventId BatchCommittedEvent = new(4106, "MsSqlBatchCommitted");
    private readonly string _name;
    private readonly MsSqlOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly MessageQueueTelemetry _telemetry;
    private readonly ILogger _logger;
    private volatile bool _disposed;

    public MsSqlMessageQueue(
        string name,
        MsSqlOptions options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        MessageQueueTelemetry telemetry,
        ILogger logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => _name;
    public string Provider => nameof(Ruya.Services.MessageQueue.MsSql);

    public async Task<string> PublishAsync<TMessage>(
        string topic,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var telemetry = _telemetry.StartPublish(
            CreateEnvelope(message, options),
            "mssql.service_broker",
            topic);
        try
        {
            var messageId = await _pipeline.ExecutePublishAsync(
                telemetry.Envelope,
                topic,
                async (env, t) => await PublishInternalAsync(env, t, cancellationToken),
                cancellationToken);
            telemetry.Complete();
            return messageId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            telemetry.Fail(ex);
            throw;
        }
    }

    private async Task<string> PublishInternalAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        CancellationToken cancellationToken) where TMessage : class
    {
        var topology = TopologyNames.ForTopic(topic);
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        topology = await EnsureTopicServiceExistsAsync(connection, topology, cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await SendEnvelopeAsync(
                connection,
                transaction,
                topology.Service,
                envelope,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            TryLogPublishCommitted(envelope.MessageId, topic, result.ConversationHandle);
            return result.MessageId;
        }
        catch
        {
            await TryRollbackAsync(transaction);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
        string topic,
        IEnumerable<TMessage> messages,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        RejectCallerAssignedBatchMessageId(options);

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return Array.Empty<string>();
        }

        var topology = TopologyNames.ForTopic(topic);
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Ensure the queue and service exist for this topic
        topology = await EnsureTopicServiceExistsAsync(connection, topology, cancellationToken);

        // Use transaction for batch
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var telemetryScopes = new List<MessageQueueTelemetry.PublishScope<TMessage>>(messageList.Count);
        var batchParent = Activity.Current?.Context ?? default;
        var messageIds = new List<string>(messageList.Count);
        var topologyByTopic = new Dictionary<string, TopologyNames.Names>(StringComparer.Ordinal)
        {
            [topic] = topology,
        };
        try
        {
            try
            {
                foreach (var message in messageList)
                {
                    var telemetry = _telemetry.StartPublish(
                        CreateEnvelope(message, options),
                        "mssql.service_broker",
                        topic,
                        batchParent);
                    telemetryScopes.Add(telemetry);
                    var envelope = telemetry.Envelope;
                    var messageId = await _pipeline.ExecutePublishAsync(
                        envelope,
                        topic,
                        async (env, destinationTopic) =>
                        {
                            if (!topologyByTopic.TryGetValue(destinationTopic, out var destinationTopology))
                            {
                                destinationTopology = await EnsureTopicServiceExistsAsync(
                                    connection,
                                    TopologyNames.ForTopic(destinationTopic),
                                    cancellationToken,
                                    transaction);
                                topologyByTopic.Add(destinationTopic, destinationTopology);
                            }

                            var result = await SendEnvelopeAsync(
                                connection,
                                transaction,
                                destinationTopology.Service,
                                env,
                                cancellationToken);
                            return result.MessageId;
                        },
                        cancellationToken);
                    messageIds.Add(messageId);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await TryRollbackAsync(transaction);
                foreach (var telemetry in telemetryScopes)
                {
                    if (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        telemetry.Fail(ex);
                    }
                }
                throw;
            }

            // Telemetry is completed only after the SQL transaction commits, and it is intentionally
            // outside the transaction catch so instrumentation cannot trigger a rollback attempt on a
            // successfully committed batch.
            foreach (var telemetry in telemetryScopes)
            {
                telemetry.Complete();
            }

            TryLogBatchCommitted(messageList.Count, topic);

            return messageIds;
        }
        finally
        {
            foreach (var telemetry in telemetryScopes)
            {
                telemetry.Dispose();
            }
        }
    }

    public IMessagePublisherBuilder<TMessage> To<TMessage>(string topic) where TMessage : class
    {
        return new MsSqlPublisherBuilder<TMessage>(this, topic);
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        string topic,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var topology = TopologyNames.ForTopic(topic);
        topology = await EnsureTopicServiceExistsAsync(connection, topology, cancellationToken);

        _logger.LogInformation(SubscriptionCreatingEvent, "Creating Service Broker subscription for topic '{Topic}'", topic);

        // Create the subscription with RECEIVE worker
        var subscription = new MsSqlSubscription<TMessage>(
            topic,
            _options,
            _serializer,
            _pipeline,
            _telemetry,
            topology,
            handler,
            options,
            _logger);

        try
        {
            await subscription.StartAsync(cancellationToken);
        }
        catch
        {
            await subscription.DisposeAsync();
            throw;
        }

        _logger.LogInformation(
            SubscriptionCreatedEvent,
            "Service Broker subscription created for topic '{Topic}' (ReceiveTimeout: {TimeoutMs}ms, MaxConcurrency: {MaxConcurrency})",
            topic, _options.ReceiveTimeoutMs, options?.MaxConcurrency ?? 1);

        return subscription;
    }

    public async Task<IMessageSubscription> SubscribeAsync<TMessage>(
        IEnumerable<string> topics,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var topicList = topics?.ToList() ?? throw new ArgumentNullException(nameof(topics));

        if (topicList.Count == 0)
        {
            throw new ArgumentException("Must specify at least one topic", nameof(topics));
        }

        // Create individual subscription for each topic
        var subscriptions = new List<IMessageSubscription>(topicList.Count);

        try
        {
            foreach (var topic in topicList)
            {
                var subscription = await SubscribeAsync(topic, handler, options, cancellationToken);
                subscriptions.Add(subscription);
            }

            // Return composite subscription that manages all individual subscriptions
            return new CompositeSubscription(subscriptions);
        }
        catch
        {
            // If any subscription fails, clean up all created subscriptions
            foreach (var sub in subscriptions)
            {
                try
                {
                    await sub.DisposeAsync();
                }
                catch
                {
                    // Swallow disposal errors during cleanup
                }
            }
            throw;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(SqlResources.CheckServiceBrokerEnabled, connection);
            cmd.CommandTimeout = 5; // Short timeout for health check
            cmd.Parameters.Add("@p0", SqlDbType.Bit).Value = false;
            var isEnabled = (bool)(await cmd.ExecuteScalarAsync(cancellationToken))!;

            return isEnabled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(HealthCheckFailedEvent, ex, "SQL Server Service Broker health check failed");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        _logger.LogInformation(DisposingEvent, "Disposing SQL Server Service Broker message bus '{Name}'", _name);

        await Task.CompletedTask;
    }

    private async Task<TopologyNames.Names> EnsureTopicServiceExistsAsync(
        SqlConnection connection,
        TopologyNames.Names topology,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        await using var cmd = new SqlCommand(SqlResources.CreateTopicService, connection, transaction);
        cmd.CommandTimeout = _options.CommandTimeoutSeconds;
        cmd.Parameters.Add("@p0", SqlDbType.NVarChar, 255).Value = topology.Topic;
        cmd.Parameters.Add("@p1", SqlDbType.NVarChar, 128).Value = topology.Queue;
        cmd.Parameters.Add("@p2", SqlDbType.NVarChar, 128).Value = topology.Service;
        cmd.Parameters.Add("@p3", SqlDbType.NVarChar, 128).Value = (object?)topology.LegacyQueue ?? DBNull.Value;
        cmd.Parameters.Add("@p4", SqlDbType.NVarChar, 128).Value = (object?)topology.LegacyService ?? DBNull.Value;
        var resolvedQueue = cmd.Parameters.Add("@p5", SqlDbType.NVarChar, 128);
        resolvedQueue.Direction = ParameterDirection.Output;
        var resolvedService = cmd.Parameters.Add("@p6", SqlDbType.NVarChar, 128);
        resolvedService.Direction = ParameterDirection.Output;
        cmd.Parameters.Add("@p7", SqlDbType.Bit).Value = false;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return topology with
        {
            Queue = (string)resolvedQueue.Value,
            Service = (string)resolvedService.Value,
        };
    }

    private async Task<SendResult> SendEnvelopeAsync<TMessage>(
        SqlConnection connection,
        SqlTransaction transaction,
        string serviceName,
        MessageEnvelope<TMessage> envelope,
        CancellationToken cancellationToken) where TMessage : class
    {
        await using var command = new SqlCommand(SqlResources.SendMessage, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        command.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = serviceName;
        command.Parameters.Add("@p1", SqlDbType.VarBinary, -1).Value = _serializer.Serialize(envelope);
        var conversationHandle = command.Parameters.Add(
            "@p2",
            SqlDbType.UniqueIdentifier);
        conversationHandle.Direction = ParameterDirection.Output;
        command.Parameters.Add("@p3", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new SendResult(envelope.MessageId, (Guid)conversationHandle.Value);
    }

    private MessageEnvelope<TMessage> CreateEnvelope<TMessage>(TMessage message, PublishOptions? options)
        where TMessage : class
    {
        return new MessageEnvelope<TMessage>
        {
            MessageId = ResolveMessageId(options),
            MessageType = typeof(TMessage).FullName ?? typeof(TMessage).Name,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = message,
            CorrelationId = options?.CorrelationId,
            CausationId = options?.CausationId,
            Source = options?.Source,
            Headers = CreateInitialHeaders(options),
            Priority = options?.Priority ?? 0,
            TimeToLive = options?.TimeToLive,
            DeliveryDelay = options?.DeliveryDelay,
            Persistent = options?.Persistent ?? true
        };
    }

    private static string ResolveMessageId(PublishOptions? options)
    {
        if (options?.MessageId is null)
        {
            return Guid.NewGuid().ToString();
        }

        if (string.IsNullOrWhiteSpace(options.MessageId))
        {
            throw new ArgumentException("MessageId cannot be empty or whitespace.", nameof(options));
        }

        return options.MessageId;
    }

    private static void RejectCallerAssignedBatchMessageId(PublishOptions? options)
    {
        if (options?.MessageId is not null)
        {
            throw new ArgumentException(
                "PublishOptions.MessageId cannot be used for batch publishing because each message needs a distinct identifier.",
                nameof(options));
        }
    }

    private static IReadOnlyDictionary<string, string> CreateInitialHeaders(PublishOptions? options)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options?.Headers is not null)
        {
            foreach (var header in options.Headers)
            {
                if (!string.Equals(header.Key, DeliveryCountHeader, StringComparison.OrdinalIgnoreCase))
                {
                    headers[header.Key] = header.Value.ToString() ?? string.Empty;
                }
            }
        }

        headers[DeliveryCountHeader] = "1";
        return headers;
    }

    private static async Task TryRollbackAsync(SqlTransaction transaction)
    {
        try
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }
        catch
        {
            // SQL Server rolls a failed transaction back when its connection is disposed. Preserve
            // the original publish/commit failure instead of masking it with a rollback exception.
        }
    }

    private void TryLogPublishCommitted(string messageId, string topic, Guid conversationHandle)
    {
        try
        {
            _logger.LogDebug(
                PublishCommittedEvent,
                "Published message {MessageId} to Service Broker topic '{Topic}' (conversation: {ConversationHandle})",
                messageId,
                topic,
                conversationHandle);
        }
        catch
        {
            // A diagnostics provider must not turn a committed publish into a reported failure.
        }
    }

    private void TryLogBatchCommitted(int count, string topic)
    {
        try
        {
            _logger.LogInformation(
                BatchCommittedEvent,
                "Published batch of {Count} messages to Service Broker topic '{Topic}'",
                count,
                topic);
        }
        catch
        {
            // A diagnostics provider must not turn a committed batch into a reported failure.
        }
    }

    private sealed record SendResult(string MessageId, Guid ConversationHandle);
}
