using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Middleware;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Telemetry;

namespace Ruya.Services.MessageQueue.MsSql;

/// <summary>
/// SQL Server Service Broker subscription implementation.
/// Each delivery is received and settled in one SQL transaction so cancellation can roll the
/// receive back instead of losing or dead-lettering an in-flight message.
/// </summary>
internal sealed class MsSqlSubscription<TMessage> : IMessageSubscription where TMessage : class
{
    private const string DeliveryCountHeader = "ruya.message_queue.delivery_count";
    private static readonly EventId PausedEvent = new(4201, "MsSqlSubscriptionPaused");
    private static readonly EventId ResumedEvent = new(4202, "MsSqlSubscriptionResumed");
    private static readonly EventId WorkerStartedEvent = new(4203, "MsSqlReceiveWorkerStarted");
    private static readonly EventId ReceiveFailedEvent = new(4204, "MsSqlReceiveFailed");
    private static readonly EventId WorkerStoppedEvent = new(4205, "MsSqlReceiveWorkerStopped");
    private static readonly EventId BrokerErrorEvent = new(4206, "MsSqlBrokerError");
    private static readonly EventId BrokerSystemMessageEvent = new(4207, "MsSqlBrokerSystemMessage");
    private static readonly EventId MessageProcessedEvent = new(4208, "MsSqlMessageProcessed");
    private static readonly EventId MessageRetriedEvent = new(4209, "MsSqlMessageRetried");
    private static readonly EventId DeliveryCapReachedEvent = new(4210, "MsSqlDeliveryCapReached");
    private static readonly EventId MessageRejectedEvent = new(4211, "MsSqlMessageRejected");
    private static readonly EventId ProcessingFailedEvent = new(4212, "MsSqlProcessingFailed");

    private readonly string _topic;
    private readonly MsSqlOptions _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly MessageQueueTelemetry _telemetry;
    private readonly TopologyNames.Names _topology;
    private readonly Func<MessageContext<TMessage>, Task<MessageResult>> _handler;
    private readonly SubscribeOptions? _subscribeOptions;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _pauseLock;
    private readonly SemaphoreSlim _pauseTransitionLock;
    private readonly CancellationTokenSource _stopTokenSource;
    private readonly TaskCompletionSource<bool> _disposeCompletion;
    private CancellationTokenRegistration _lifetimeRegistration;
    private Task? _receiveWorker;
    private int _pauseCount;
    private volatile bool _isActive;
    private int _disposeState;

    public MsSqlSubscription(
        string topic,
        MsSqlOptions options,
        IMessageSerializer serializer,
        MiddlewarePipeline pipeline,
        MessageQueueTelemetry telemetry,
        TopologyNames.Names topology,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? subscribeOptions,
        ILogger logger)
    {
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _subscribeOptions = subscribeOptions;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ValidateDeliveryPolicy(subscribeOptions, options);
        _pauseLock = new SemaphoreSlim(1, 1);
        _pauseTransitionLock = new SemaphoreSlim(1, 1);
        _stopTokenSource = new CancellationTokenSource();
        _disposeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SubscriptionId = Guid.NewGuid().ToString();
    }

    public string SubscriptionId { get; }

    public IReadOnlyList<string> Topics => new[] { _topic };

    public bool IsActive => _isActive && Volatile.Read(ref _disposeState) == 0;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_receiveWorker is not null)
        {
            throw new InvalidOperationException("Subscription is already started");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _isActive = true;
        _lifetimeRegistration = cancellationToken.UnsafeRegister(
            static state => _ = ((MsSqlSubscription<TMessage>)state!).StopFromLifetimeAsync(),
            this);

        var workerCount = _subscribeOptions?.MaxConcurrency ?? 1;
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => ReceiveMessagesLoopAsync(_stopTokenSource.Token))
            .ToArray();
        _receiveWorker = Task.WhenAll(workers);
        return Task.CompletedTask;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        await _pauseTransitionLock.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                return;
            }

            if (_pauseCount == 1)
            {
                return;
            }

            await _pauseLock.WaitAsync(cancellationToken);
            _pauseCount = 1;
            _isActive = false;
            _logger.LogInformation(PausedEvent, "Service Broker subscription '{SubscriptionId}' paused", SubscriptionId);
        }
        finally
        {
            _pauseTransitionLock.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(MsSqlSubscription<TMessage>));
        }

        if (_stopTokenSource.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The subscription lifetime has ended and cannot be resumed. Create a new subscription.");
        }

        await _pauseTransitionLock.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(MsSqlSubscription<TMessage>));
            }
            if (_stopTokenSource.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "The subscription lifetime has ended and cannot be resumed. Create a new subscription.");
            }

            if (_pauseCount == 0)
            {
                return;
            }

            _pauseCount = 0;
            _isActive = true;
            _pauseLock.Release();
            _logger.LogInformation(ResumedEvent, "Service Broker subscription '{SubscriptionId}' resumed", SubscriptionId);
        }
        finally
        {
            _pauseTransitionLock.Release();
        }
    }

    private async Task ReceiveMessagesLoopAsync(CancellationToken cancellationToken)
    {
        var queueName = _topology.Queue;
        var consecutiveFailures = 0;
        _logger.LogInformation(
            WorkerStartedEvent,
            "Service Broker receive worker started for queue '{QueueName}'",
            queueName);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _pauseLock.WaitAsync(cancellationToken);
                _pauseLock.Release();

                if (!_isActive)
                {
                    continue;
                }

                var received = await ReceiveAndProcessOneAsync(queueName, cancellationToken);
                consecutiveFailures = 0;
                if (!received)
                {
                    await Task.Delay(_options.PollingIntervalMs, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                _logger.LogError(ReceiveFailedEvent, ex, "Error receiving messages from Service Broker queue '{QueueName}'", queueName);
                try
                {
                    await Task.Delay(GetReceiveFailureDelay(consecutiveFailures), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation(WorkerStoppedEvent, "Service Broker receive worker stopped for queue '{QueueName}'", queueName);
    }

    private async Task<bool> ReceiveAndProcessOneAsync(string queueName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var received = await ReceiveOneAsync(connection, transaction, queueName, cancellationToken);
            if (received is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (IsSystemMessageType(received.MessageTypeName))
            {
                if (IsBrokerErrorMessage(received.MessageTypeName))
                {
                    _logger.LogWarning(
                        BrokerErrorEvent,
                        "Service Broker reported an asynchronous delivery error on conversation '{ConversationHandle}'",
                        received.ConversationHandle);
                }
                else
                {
                    _logger.LogDebug(
                        BrokerSystemMessageEvent,
                        "Acknowledging system message type '{MessageType}' on conversation '{ConversationHandle}'",
                        received.MessageTypeName,
                        received.ConversationHandle);
                }
                await EndConversationAsync(connection, transaction, received.ConversationHandle, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }

            if (received.MessageTypeName != "RuyaServicesMessageQueueMessage" || received.MessageBody is null)
            {
                await HandleMalformedMessageAsync(
                    connection,
                    transaction,
                    received,
                    cancellationToken);
                return true;
            }

            await HandleMessageAsync(
                connection,
                transaction,
                received.ConversationHandle,
                received.MessageBody,
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction);
            throw;
        }
        catch
        {
            await TryRollbackAsync(transaction);
            throw;
        }
    }

    private async Task<ReceivedMessage?> ReceiveOneAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string queueName,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(SqlResources.ReceiveMessage, connection, transaction)
        {
            CommandTimeout = Math.Max(5, (_options.ReceiveTimeoutMs / 1000) + 5),
        };
        command.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = queueName;
        command.Parameters.Add("@p1", SqlDbType.Int).Value = _options.ReceiveTimeoutMs;
        command.Parameters.Add("@p2", SqlDbType.Bit).Value = false;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReceivedMessage(
            reader.GetGuid(0),
            reader.GetString(1),
            await reader.IsDBNullAsync(2, cancellationToken) ? null : (byte[])reader.GetValue(2));
    }

    private async Task HandleMessageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid conversationHandle,
        byte[] messageBody,
        CancellationToken cancellationToken)
    {
        using var telemetry = _telemetry.StartDelivery(
            "mssql.service_broker",
            _topic,
            _subscribeOptions?.ConsumerGroup);
        MessageEnvelope<TMessage>? envelope = null;
        var deliveryCount = 1;

        MessageResult result;
        try
        {
            envelope = _serializer.Deserialize<MessageEnvelope<TMessage>>(messageBody);
            ArgumentNullException.ThrowIfNull(envelope);
            deliveryCount = GetDeliveryCount(envelope);
            telemetry.AttachEnvelope(envelope, deliveryCount);

            var context = new MessageContext<TMessage>
            {
                Envelope = envelope,
                Topic = _topic,
                ConsumerGroup = _subscribeOptions?.ConsumerGroup,
                DeliveryCount = deliveryCount,
                CancellationToken = cancellationToken,
            };

            result = await _pipeline.ExecuteConsumeAsync(context, _handler, cancellationToken);
            ArgumentNullException.ThrowIfNull(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction);
            telemetry.Cancel();
            throw;
        }
        catch (Exception processingException)
        {
            await SettleProcessingFailureAsync(
                connection,
                transaction,
                conversationHandle,
                messageBody,
                envelope,
                deliveryCount,
                processingException,
                telemetry,
                cancellationToken);
            return;
        }

        // Settlement is intentionally outside the handler/deserialize catch. A broker or commit
        // failure must roll the RECEIVE transaction back and remain eligible for redelivery; it
        // must never be reinterpreted as poison from a handler that already succeeded.
        try
        {
            var appliedStatus = await ApplyResultAsync(
                connection,
                transaction,
                conversationHandle,
                messageBody,
                envelope!,
                result,
                deliveryCount,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            LogAppliedResult(envelope!, result, appliedStatus, deliveryCount);
            telemetry.Complete(appliedStatus);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction);
            telemetry.Cancel();
            throw;
        }
        catch (Exception settlementException)
        {
            await TryRollbackAsync(transaction);
            telemetry.Unhandled(settlementException);
            throw;
        }
    }

    private async Task SettleProcessingFailureAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid conversationHandle,
        byte[] messageBody,
        MessageEnvelope<TMessage>? envelope,
        int deliveryCount,
        Exception processingException,
        MessageQueueTelemetry.DeliveryScope telemetry,
        CancellationToken cancellationToken)
    {
        try
        {
            var shouldRetry = _subscribeOptions?.RequeueOnException == true &&
                envelope is not null &&
                deliveryCount < ResolveMaxDeliveryCount(_subscribeOptions, _options);

            if (shouldRetry)
            {
                await Task.Delay(GetRetryDelay(deliveryCount, _subscribeOptions), cancellationToken);
                await ReplaceForRetryAsync(
                    connection,
                    transaction,
                    conversationHandle,
                    IncrementDeliveryCount(envelope!, deliveryCount + 1),
                    cancellationToken);
            }
            else
            {
                await MoveToDeadLetterAsync(
                    connection,
                    transaction,
                    conversationHandle,
                    envelope?.MessageId ?? conversationHandle.ToString("D"),
                    messageBody,
                    processingException.GetType().Name,
                    deliveryCount,
                    envelope?.Timestamp,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            telemetry.Unhandled(processingException);
            TryLogProcessingFailure(processingException, deliveryCount, shouldRetry);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction);
            telemetry.Cancel();
            throw;
        }
        catch (Exception settlementException)
        {
            await TryRollbackAsync(transaction);
            telemetry.Unhandled(settlementException);
            throw new AggregateException(
                "Message processing failed and its broker settlement also failed.",
                processingException,
                settlementException);
        }
    }

    private async Task HandleMalformedMessageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ReceivedMessage received,
        CancellationToken cancellationToken)
    {
        using var telemetry = _telemetry.StartDelivery(
            "mssql.service_broker",
            _topic,
            _subscribeOptions?.ConsumerGroup);
        var exception = new InvalidDataException(
            received.MessageBody is null
                ? $"Application message type '{received.MessageTypeName}' has no body."
                : $"Unexpected application message type '{received.MessageTypeName}'.");

        try
        {
            await MoveToDeadLetterAsync(
                connection,
                transaction,
                received.ConversationHandle,
                received.ConversationHandle.ToString("D"),
                received.MessageBody ?? Array.Empty<byte>(),
                exception.Message,
                deliveryCount: 1,
                originalTimestamp: null,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            telemetry.Unhandled(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction);
            telemetry.Cancel();
            throw;
        }
        catch (Exception settlementException)
        {
            await TryRollbackAsync(transaction);
            telemetry.Unhandled(settlementException);
            throw;
        }
    }

    private static bool IsSystemMessageType(string messageTypeName)
    {
        return IsBrokerErrorMessage(messageTypeName) ||
            string.Equals(
                messageTypeName,
                "https://schemas.microsoft.com/SQL/ServiceBroker/EndDialog",
                StringComparison.Ordinal) ||
            string.Equals(
                messageTypeName,
                "https://schemas.microsoft.com/SQL/ServiceBroker/DialogTimer",
                StringComparison.Ordinal);
    }

    private static bool IsBrokerErrorMessage(string messageTypeName)
    {
        return string.Equals(
            messageTypeName,
            "http://schemas.microsoft.com/SQL/ServiceBroker/Error",
            StringComparison.Ordinal);
    }

    private async Task<MessageStatus> ApplyResultAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid conversationHandle,
        byte[] messageBody,
        MessageEnvelope<TMessage> envelope,
        MessageResult result,
        int deliveryCount,
        CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case MessageStatus.Success:
                await EndConversationAsync(connection, transaction, conversationHandle, cancellationToken);
                return MessageStatus.Success;

            case MessageStatus.Reject:
                await MoveToDeadLetterAsync(
                    connection,
                    transaction,
                    conversationHandle,
                    envelope.MessageId,
                    messageBody,
                    result.Reason ?? "Message rejected",
                    deliveryCount,
                    envelope.Timestamp,
                    cancellationToken);
                return MessageStatus.Reject;

            case MessageStatus.Retry:
                if (deliveryCount >= ResolveMaxDeliveryCount(_subscribeOptions, _options))
                {
                    await MoveToDeadLetterAsync(
                        connection,
                        transaction,
                        conversationHandle,
                        envelope.MessageId,
                        messageBody,
                        result.Reason ?? "Maximum delivery count exceeded",
                        deliveryCount,
                        envelope.Timestamp,
                        cancellationToken);
                    return MessageStatus.Reject;
                }

                await Task.Delay(GetRetryDelay(deliveryCount, _subscribeOptions), cancellationToken);
                await ReplaceForRetryAsync(
                    connection,
                    transaction,
                    conversationHandle,
                    IncrementDeliveryCount(envelope, deliveryCount + 1),
                    cancellationToken);
                return MessageStatus.Retry;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown message result status.");
        }
    }

    private void LogAppliedResult(
        MessageEnvelope<TMessage> envelope,
        MessageResult requestedResult,
        MessageStatus appliedStatus,
        int deliveryCount)
    {
        try
        {
            if (appliedStatus == MessageStatus.Success)
            {
                _logger.LogDebug(
                    MessageProcessedEvent,
                    "Message {MessageId} processed successfully and its Service Broker transaction committed",
                    envelope.MessageId);
                return;
            }

            if (appliedStatus == MessageStatus.Retry)
            {
                _logger.LogWarning(
                    MessageRetriedEvent,
                    "Message {MessageId} committed for retry after attempt {DeliveryCount}. Reason: {Reason}",
                    envelope.MessageId,
                    deliveryCount,
                    requestedResult.Reason);
                return;
            }

            if (requestedResult.Status == MessageStatus.Retry)
            {
                _logger.LogWarning(
                    DeliveryCapReachedEvent,
                    "Message {MessageId} reached the delivery cap at attempt {DeliveryCount}; its DLQ transaction committed.",
                    envelope.MessageId,
                    deliveryCount);
                return;
            }

            _logger.LogWarning(
                MessageRejectedEvent,
                "Message {MessageId} was rejected and its DLQ transaction committed. Reason: {Reason}",
                envelope.MessageId,
                requestedResult.Reason);
        }
        catch
        {
            // A diagnostics provider must not turn committed settlement into a reported failure.
        }
    }

    private void TryLogProcessingFailure(Exception exception, int deliveryCount, bool retried)
    {
        try
        {
            _logger.LogError(
                ProcessingFailedEvent,
                exception,
                "Unhandled message failure on Service Broker topic '{Topic}' (DeliveryCount={DeliveryCount}, Retried={Retried})",
                _topic,
                deliveryCount,
                retried);
        }
        catch
        {
            // A diagnostics provider must not turn committed settlement into a reported failure.
        }
    }

    private async Task ReplaceForRetryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid originalConversationHandle,
        MessageEnvelope<TMessage> envelope,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(SqlResources.SendMessage, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        command.Parameters.Add("@p0", SqlDbType.NVarChar, 128).Value = _topology.Service;
        command.Parameters.Add("@p1", SqlDbType.VarBinary, -1).Value = _serializer.Serialize(envelope);
        command.Parameters.Add("@p2", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        command.Parameters.Add("@p3", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EndConversationAsync(connection, transaction, originalConversationHandle, cancellationToken);
    }

    private static MessageEnvelope<TMessage> IncrementDeliveryCount(
        MessageEnvelope<TMessage> envelope,
        int nextDeliveryCount)
    {
        var headers = envelope.Headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(envelope.Headers, StringComparer.OrdinalIgnoreCase);
        headers[DeliveryCountHeader] = nextDeliveryCount.ToString(CultureInfo.InvariantCulture);

        return new MessageEnvelope<TMessage>
        {
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            Source = envelope.Source,
            MessageType = envelope.MessageType,
            Timestamp = envelope.Timestamp,
            Headers = headers,
            Payload = envelope.Payload,
            Priority = envelope.Priority,
            TimeToLive = envelope.TimeToLive,
            DeliveryDelay = envelope.DeliveryDelay,
            Persistent = envelope.Persistent,
        };
    }

    private static int GetDeliveryCount(MessageEnvelope<TMessage> envelope)
    {
        if (envelope.Headers is not null &&
            envelope.Headers.TryGetValue(DeliveryCountHeader, out var rawCount) &&
            int.TryParse(rawCount, NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
            count > 0)
        {
            return count;
        }

        return 1;
    }

    private async Task EndConversationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid conversationHandle,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(SqlResources.EndConversation, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        command.Parameters.Add("@p0", SqlDbType.UniqueIdentifier).Value = conversationHandle;
        command.Parameters.Add("@p1", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MoveToDeadLetterAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid conversationHandle,
        string messageId,
        byte[] messageBody,
        string errorMessage,
        int deliveryCount,
        DateTimeOffset? originalTimestamp,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(SqlResources.InsertDeadLetter, connection, transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };
        command.Parameters.Add("@p0", SqlDbType.NVarChar, -1).Value = messageId;
        command.Parameters.Add("@p1", SqlDbType.NVarChar, 255).Value = _topic;
        command.Parameters.Add("@p2", SqlDbType.VarBinary, -1).Value = messageBody;
        command.Parameters.Add("@p3", SqlDbType.NVarChar, -1).Value = errorMessage;
        command.Parameters.Add("@p4", SqlDbType.Int).Value = deliveryCount;
        command.Parameters.Add("@p5", SqlDbType.DateTime2).Value = originalTimestamp?.UtcDateTime ?? DateTime.UtcNow;
        command.Parameters.Add("@p6", SqlDbType.Bit).Value = false;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EndConversationAsync(connection, transaction, conversationHandle, cancellationToken);
    }

    private static int ResolveMaxDeliveryCount(SubscribeOptions? options, MsSqlOptions providerOptions)
    {
        return options?.MaxDeliveryCount
            ?? (options?.RetryPolicy is { } policy
                ? checked(policy.MaxRetryAttempts + 1)
                : providerOptions.MaxDeliveryAttempts);
    }

    private static TimeSpan GetRetryDelay(int deliveryCount, SubscribeOptions? options)
    {
        var policy = options?.RetryPolicy ?? new RetryPolicy();
        var multiplier = policy.UseExponentialBackoff
            ? Math.Pow(policy.BackoffMultiplier, Math.Max(0, deliveryCount - 1))
            : 1d;
        var ticks = (long)Math.Min(policy.InitialDelay.Ticks * multiplier, policy.MaxDelay.Ticks);
        var delay = TimeSpan.FromTicks(ticks);
        if (policy.UseJitter)
        {
            delay = ApplyJitter(delay, policy.MaxDelay);
        }

        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
    }

    private static TimeSpan GetReceiveFailureDelay(int consecutiveFailures)
    {
        var exponent = Math.Min(Math.Max(0, consecutiveFailures - 1), 5);
        var delay = TimeSpan.FromSeconds(Math.Pow(2d, exponent));
        return ApplyJitter(delay, TimeSpan.FromSeconds(30));
    }

    private static TimeSpan ApplyJitter(TimeSpan delay, TimeSpan maximum)
    {
        const double MinimumFactor = 0.8d;
        const double JitterRange = 0.4d;
        var randomFraction = RandomNumberGenerator.GetInt32(0, 10_001) / 10_000d;
        var jitteredTicks = delay.Ticks * (MinimumFactor + (randomFraction * JitterRange));
        var boundedTicks = (long)Math.Min(jitteredTicks, maximum.Ticks);
        return TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(1).Ticks, boundedTicks));
    }

    private static void ValidateDeliveryPolicy(SubscribeOptions? options, MsSqlOptions providerOptions)
    {
        var maxDeliveryCount = ResolveMaxDeliveryCount(options, providerOptions);
        if (maxDeliveryCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDeliveryCount must be at least one.");
        }

        if ((options?.MaxConcurrency ?? 1) < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxConcurrency must be at least one.");
        }

        var policy = options?.RetryPolicy ?? new RetryPolicy();
        if (policy.MaxRetryAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryPolicy.MaxRetryAttempts must be at least one.");
        }
        if (policy.InitialDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryPolicy.InitialDelay must be greater than zero.");
        }
        if (policy.MaxDelay < policy.InitialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryPolicy.MaxDelay must be at least InitialDelay.");
        }
        if (policy.UseExponentialBackoff && policy.BackoffMultiplier < 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryPolicy.BackoffMultiplier must be at least one.");
        }
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
            // The connection can close concurrently during host shutdown. A failed transaction is
            // rolled back by SQL Server when the connection is disposed.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            await _disposeCompletion.Task;
            return;
        }

        try
        {
            _isActive = false;
            await _pauseTransitionLock.WaitAsync(CancellationToken.None);
            try
            {
                await _stopTokenSource.CancelAsync();

                if (_receiveWorker is not null)
                {
                    try
                    {
                        await _receiveWorker;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown.
                    }
                }
            }
            finally
            {
                _pauseTransitionLock.Release();
            }

            await _lifetimeRegistration.DisposeAsync();
            _stopTokenSource.Dispose();
            _pauseLock.Dispose();
            _pauseTransitionLock.Dispose();
            _disposeCompletion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }

    private async Task StopFromLifetimeAsync()
    {
        _isActive = false;
        await _stopTokenSource.CancelAsync();
    }

    private sealed record ReceivedMessage(
        Guid ConversationHandle,
        string MessageTypeName,
        byte[]? MessageBody);
}
