using System;
using System.Collections.Generic;
using System.Data;
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

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// SQL Server Service Broker implementation of IMessageQueue
/// </summary>
internal sealed class MsSqlMessageQueue : IMessageQueue
{
    private readonly string _name;
    private readonly MsSqlOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly ILogger _logger;
    private volatile bool _disposed;

    public MsSqlMessageQueue(
        string name,
        MsSqlOptions options,
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware> middlewares,
        ILogger logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => _name;
    public string Provider => "MsSql";

    public async Task<string> PublishAsync<TMessage>(
        string topic,
        TMessage message,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var envelope = CreateEnvelope(message, options);

        return await _pipeline.ExecutePublishAsync(
            envelope,
            topic,
            async (env, t) => await PublishInternalAsync(env, t, cancellationToken),
            cancellationToken);
    }

    private async Task<string> PublishInternalAsync<TMessage>(
        MessageEnvelope<TMessage> envelope,
        string topic,
        CancellationToken cancellationToken) where TMessage : class
    {
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Ensure the queue and service exist for this topic
        await EnsureTopicServiceExistsAsync(connection, topic, cancellationToken);

        var serviceName = GetServiceName(topic);
        var payload = _serializer.Serialize(envelope);

        // Begin dialog conversation
        var beginDialogSql = @"
            BEGIN DIALOG CONVERSATION @ConversationHandle
                FROM SERVICE @ServiceName
                TO SERVICE @ServiceName
                ON CONTRACT [RuyaServicesMessageQueueContract]
                WITH ENCRYPTION = OFF;

            -- Send the message
            SEND ON CONVERSATION @ConversationHandle
                MESSAGE TYPE [RuyaServicesMessageQueueMessage] (@Payload);

            -- End conversation (fire-and-forget pattern)
            END CONVERSATION @ConversationHandle;

            SELECT @ConversationHandle;";

        await using var cmd = new SqlCommand(beginDialogSql, connection);
        cmd.CommandTimeout = _options.CommandTimeoutSeconds;
        cmd.Parameters.Add("@ConversationHandle", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        cmd.Parameters.AddWithValue("@ServiceName", serviceName);
        cmd.Parameters.AddWithValue("@Payload", payload);

        var conversationHandle = (Guid)(await cmd.ExecuteScalarAsync(cancellationToken))!;

        _logger.LogDebug(
            "Published message {MessageId} to Service Broker topic '{Topic}' (conversation: {ConversationHandle})",
            envelope.MessageId, topic, conversationHandle);

        return envelope.MessageId;
    }

    public async Task<IReadOnlyList<string>> PublishBatchAsync<TMessage>(
        string topic,
        IEnumerable<TMessage> messages,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return Array.Empty<string>();
        }

        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Ensure the queue and service exist for this topic
        await EnsureTopicServiceExistsAsync(connection, topic, cancellationToken);

        // Use transaction for batch
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var messageIds = new List<string>();
            var serviceName = GetServiceName(topic);

            foreach (var message in messageList)
            {
                var envelope = CreateEnvelope(message, options);
                var payload = _serializer.Serialize(envelope);

                var sql = @"
                    BEGIN DIALOG CONVERSATION @ConversationHandle
                        FROM SERVICE @ServiceName
                        TO SERVICE @ServiceName
                        ON CONTRACT [RuyaServicesMessageQueueContract]
                        WITH ENCRYPTION = OFF;

                    SEND ON CONVERSATION @ConversationHandle
                        MESSAGE TYPE [RuyaServicesMessageQueueMessage] (@Payload);

                    END CONVERSATION @ConversationHandle;";

                await using var cmd = new SqlCommand(sql, connection, transaction);
                cmd.CommandTimeout = _options.CommandTimeoutSeconds;
                cmd.Parameters.Add("@ConversationHandle", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
                cmd.Parameters.AddWithValue("@ServiceName", serviceName);
                cmd.Parameters.AddWithValue("@Payload", payload);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
                messageIds.Add(envelope.MessageId);
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Published batch of {Count} messages to Service Broker topic '{Topic}'",
                messageList.Count, topic);

            return messageIds;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
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

        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Ensure the queue and service exist for this topic
        await EnsureTopicServiceExistsAsync(connection, topic, cancellationToken);

        _logger.LogInformation("Creating Service Broker subscription for topic '{Topic}'", topic);

        // Create the subscription with RECEIVE worker
        var subscription = new MsSqlSubscription<TMessage>(
            topic,
            _options,
            _serializer,
            _pipeline,
            handler,
            options,
            _logger);

        // Start the receive worker
        await subscription.StartAsync(cancellationToken);

        _logger.LogInformation(
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

            var sql = "SELECT is_broker_enabled FROM sys.databases WHERE name = DB_NAME()";
            await using var cmd = new SqlCommand(sql, connection);
            cmd.CommandTimeout = 5; // Short timeout for health check
            var isEnabled = (bool)(await cmd.ExecuteScalarAsync(cancellationToken))!;

            return isEnabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL Server Service Broker health check failed");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        _logger.LogInformation("Disposing SQL Server Service Broker message bus '{Name}'", _name);

        await Task.CompletedTask;
    }

    private async Task EnsureTopicServiceExistsAsync(SqlConnection connection, string topic, CancellationToken cancellationToken)
    {
        var sql = "EXEC [dbo].[RuyaServicesMessageQueue_CreateTopicService] @TopicName";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = _options.CommandTimeoutSeconds;
        cmd.Parameters.AddWithValue("@TopicName", topic);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private string GetServiceName(string topic) =>
        $"RuyaServicesMessageQueueService_{topic.Replace(".", "_")}";

    private string GetQueueName(string topic) =>
        $"RuyaServicesMessageQueueQueue_{topic.Replace(".", "_")}";

    private MessageEnvelope<TMessage> CreateEnvelope<TMessage>(TMessage message, PublishOptions? options)
        where TMessage : class
    {
        return new MessageEnvelope<TMessage>
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = typeof(TMessage).FullName ?? typeof(TMessage).Name,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = message,
            CorrelationId = options?.CorrelationId,
            CausationId = options?.CausationId,
            Source = options?.Source,
            Headers = options?.Headers?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString() ?? string.Empty) ?? new Dictionary<string, string>(),
            Priority = options?.Priority ?? 0,
            TimeToLive = options?.TimeToLive,
            DeliveryDelay = options?.DeliveryDelay,
            Persistent = options?.Persistent ?? true
        };
    }
}
