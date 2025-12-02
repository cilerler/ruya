using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Serialization;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// SQL Server Service Broker subscription implementation
/// </summary>
internal sealed class MsSqlSubscription<TMessage> : IMessageSubscription where TMessage : class
{
    private readonly string _topic;
    private readonly MsSqlOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly Func<MessageContext<TMessage>, Task<MessageResult>> _handler;
    private readonly SubscribeOptions? _subscribeOptions;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly SemaphoreSlim _pauseLock;
    private int _pauseCount = 0;  // Track pause state to prevent deadlock
    private readonly CancellationTokenSource _stopTokenSource;

    // Track ALL processing tasks globally to await them during disposal
    private readonly System.Collections.Concurrent.ConcurrentBag<Task> _allProcessingTasks = new();

    private Task? _receiveWorker;
    private volatile bool _isActive;
    private volatile bool _disposed;

    public MsSqlSubscription(
        string topic,
        MsSqlOptions options,
        IMessageSerializer serializer,
        MiddlewarePipeline pipeline,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? subscribeOptions,
        ILogger logger)
    {
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _subscribeOptions = subscribeOptions;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var maxConcurrency = subscribeOptions?.MaxConcurrency ?? 1;
        _concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _pauseLock = new SemaphoreSlim(1, 1);
        _stopTokenSource = new CancellationTokenSource();

        SubscriptionId = Guid.NewGuid().ToString();
    }

    public string SubscriptionId { get; }

    public IReadOnlyList<string> Topics => new[] { _topic };

    public bool IsActive => _isActive && !_disposed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_receiveWorker != null)
        {
            throw new InvalidOperationException("Subscription is already started");
        }

        _isActive = true;

        // Start background worker to receive messages
        _receiveWorker = Task.Run(async () => await ReceiveMessagesLoopAsync(_stopTokenSource.Token), cancellationToken);

        return Task.CompletedTask;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        // Use Interlocked to prevent multiple pause calls from acquiring lock multiple times (deadlock prevention)
        if (Interlocked.CompareExchange(ref _pauseCount, 1, 0) == 0)
        {
            await _pauseLock.WaitAsync(cancellationToken);
            _isActive = false;
            _logger.LogInformation("Service Broker subscription '{SubscriptionId}' paused", SubscriptionId);
        }
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MsSqlSubscription<TMessage>));
        }

        // Use Interlocked to prevent multiple resume calls from releasing lock multiple times
        if (Interlocked.CompareExchange(ref _pauseCount, 0, 1) == 1)
        {
            _isActive = true;
            _pauseLock.Release();
            _logger.LogInformation("Service Broker subscription '{SubscriptionId}' resumed", SubscriptionId);
        }

        return Task.CompletedTask;
    }

    private async Task ReceiveMessagesLoopAsync(CancellationToken cancellationToken)
    {
        var queueName = GetQueueName(_topic);

        _logger.LogInformation(
            "Service Broker receive worker started for queue '{QueueName}'",
            queueName);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Wait if paused (blocks efficiently without consuming CPU)
            await _pauseLock.WaitAsync(cancellationToken);
            _pauseLock.Release();

            if (!_isActive)
            {
                continue;
            }

            try
            {
                await using var connection = new SqlConnection(_options.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                // RECEIVE messages from Service Broker queue
                var receiveSql = _options.ReceiveTimeoutMs > 0
                    ? $@"
                        WAITFOR (
                            RECEIVE TOP(@BatchSize)
                                conversation_handle,
                                message_type_name,
                                message_body,
                                message_sequence_number
                            FROM [{queueName}]
                        ), TIMEOUT @TimeoutMs;"
                    : $@"
                        RECEIVE TOP(@BatchSize)
                            conversation_handle,
                            message_type_name,
                            message_body,
                            message_sequence_number
                        FROM [{queueName}];";

                await using var cmd = new SqlCommand(receiveSql, connection);
                cmd.CommandTimeout = (_options.ReceiveTimeoutMs / 1000) + 5; // Add buffer

                cmd.Parameters.AddWithValue("@BatchSize", _options.BatchSize);
                if (_options.ReceiveTimeoutMs > 0)
                {
                    cmd.Parameters.AddWithValue("@TimeoutMs", _options.ReceiveTimeoutMs);
                }

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                var messageCount = 0;
                var tasks = new List<Task>();

                while (await reader.ReadAsync(cancellationToken))
                {
                    var conversationHandle = reader.GetGuid(0);
                    var messageTypeName = reader.GetString(1);
                    var messageBody = reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2);
                    var sequenceNumber = reader.GetInt64(3);

                    messageCount++;

                    // Skip system messages
                    if (messageTypeName != "RuyaServicesMessageQueueMessage" || messageBody == null)
                    {
                        _logger.LogDebug(
                            "Skipping system message type '{MessageType}' on conversation '{ConversationHandle}'",
                            messageTypeName, conversationHandle);

                        // End system message conversations
                        await EndConversationAsync(connection, conversationHandle, cancellationToken);
                        continue;
                    }

                    // Process message with concurrency control
                    var task = Task.Run(async () =>
                    {
                        await _concurrencySemaphore.WaitAsync(cancellationToken);
                        try
                        {
                            await HandleMessageAsync(connection, conversationHandle, messageBody, cancellationToken);
                        }
                        finally
                        {
                            _concurrencySemaphore.Release();
                        }
                    }, cancellationToken);

                    tasks.Add(task);
                    _allProcessingTasks.Add(task);  // Track globally for disposal
                }

                // Wait for all messages in this batch to be processed
                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks);
                }

                // If no messages received, add small delay to avoid tight loop
                if (messageCount == 0)
                {
                    await Task.Delay(_options.PollingIntervalMs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving messages from Service Broker queue '{QueueName}'", queueName);
                await Task.Delay(1000, cancellationToken); // Back off on error
            }
        }

        _logger.LogInformation("Service Broker receive worker stopped for queue '{QueueName}'", queueName);
    }

    private async Task HandleMessageAsync(
        SqlConnection connection,
        Guid conversationHandle,
        byte[] messageBody,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = _serializer.Deserialize<MessageEnvelope<TMessage>>(messageBody);

            var context = new MessageContext<TMessage>
            {
                Envelope = envelope,
                Topic = _topic,
                ConsumerGroup = _subscribeOptions?.ConsumerGroup,
                DeliveryCount = 1, // Service Broker doesn't track delivery count natively
                CancellationToken = cancellationToken
            };

            var result = await _pipeline.ExecuteConsumeAsync(context, _handler, cancellationToken);

            if (result.Status == MessageStatus.Success)
            {
                // End the conversation (acknowledge)
                await EndConversationAsync(connection, conversationHandle, cancellationToken);

                _logger.LogDebug(
                    "Message {MessageId} processed successfully and conversation ended",
                    envelope.MessageId);
            }
            else if (result.Status == MessageStatus.Reject)
            {
                // Move to dead letter
                await MoveToDeadLetterAsync(connection, conversationHandle, envelope.MessageId, messageBody,
                    result.Reason ?? "Message rejected", cancellationToken);

                _logger.LogWarning(
                    "Message {MessageId} rejected and moved to dead letter: {Reason}",
                    envelope.MessageId, result.Reason);
            }
            else // Retry
            {
                // For Service Broker, we can't easily retry without additional infrastructure
                // For now, log and end conversation (message is lost)
                // In production, you'd want to implement a retry mechanism
                _logger.LogWarning(
                    "Message {MessageId} requested retry, but Service Broker doesn't support native retry. " +
                    "Consider implementing a retry queue pattern. Reason: {Reason}",
                    envelope.MessageId, result.Reason);

                await EndConversationAsync(connection, conversationHandle, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from Service Broker");

            // End conversation to prevent poison message
            await EndConversationAsync(connection, conversationHandle, cancellationToken);
        }
    }

    private async Task EndConversationAsync(SqlConnection connection, Guid conversationHandle, CancellationToken cancellationToken)
    {
        try
        {
            var sql = "END CONVERSATION @ConversationHandle";
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@ConversationHandle", conversationHandle);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending conversation '{ConversationHandle}'", conversationHandle);
        }
    }

    private async Task MoveToDeadLetterAsync(
        SqlConnection connection,
        Guid conversationHandle,
        string messageId,
        byte[] messageBody,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var sql = @"
                INSERT INTO [dbo].[RuyaServicesMessageQueueDeadLetter]
                    ([MessageId], [TopicName], [MessagePayload], [ErrorMessage], [DeliveryAttempts], [OriginalTimestamp])
                VALUES
                    (@MessageId, @TopicName, @MessagePayload, @ErrorMessage, 1, @OriginalTimestamp);

                END CONVERSATION @ConversationHandle;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@MessageId", Guid.Parse(messageId));
            cmd.Parameters.AddWithValue("@TopicName", _topic);
            cmd.Parameters.AddWithValue("@MessagePayload", messageBody);
            cmd.Parameters.AddWithValue("@ErrorMessage", errorMessage);
            cmd.Parameters.AddWithValue("@OriginalTimestamp", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@ConversationHandle", conversationHandle);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving message to dead letter");
        }
    }

    private string GetQueueName(string topic) =>
        $"RuyaServicesMessageQueueQueue_{topic.Replace(".", "_")}";

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing Service Broker subscription '{SubscriptionId}'", SubscriptionId);

        // Set flags FIRST to prevent new operations
        _disposed = true;
        _isActive = false;

        // Stop receiving messages
        _stopTokenSource.Cancel();

        // Wait for worker to complete
        if (_receiveWorker != null)
        {
            try
            {
                await _receiveWorker;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // Wait for ALL processing tasks to complete before disposing semaphore
        if (_allProcessingTasks.Count > 0)
        {
            _logger.LogDebug("Waiting for {Count} processing tasks to complete for subscription '{SubscriptionId}'",
                _allProcessingTasks.Count, SubscriptionId);

            try
            {
                await Task.WhenAll(_allProcessingTasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Processing tasks cancelled during disposal");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for processing tasks during disposal for subscription '{SubscriptionId}'",
                    SubscriptionId);
            }
        }

        // NOW safe to dispose resources (all tasks are fully stopped)
        _concurrencySemaphore.Dispose();
        _pauseLock.Dispose();
        _stopTokenSource.Dispose();

        _logger.LogInformation("Service Broker subscription '{SubscriptionId}' disposed", SubscriptionId);
    }
}
