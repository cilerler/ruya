using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Middleware;

using Ruya.Services.MessageQueue.Utilities;
using Ruya.Services.MessageQueue.Telemetry;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// Represents an active in-memory subscription
/// </summary>
internal sealed class InMemorySubscription<TMessage> : IMessageSubscription where TMessage : class
{
    private readonly string _topic;
    private readonly string _consumerGroup;
    private readonly ConsumerGroupBuffer _consumerGroupBuffer;
    private readonly Action _releaseConsumerGroup;
    private readonly Func<MessageContext<TMessage>, Task<MessageResult>> _handler;
    private readonly SubscribeOptions? _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly InMemoryOptions _config;
    private readonly string _queueName;
    private readonly IInMemoryDeadLetterStore _deadLetterStore;
    private readonly MessageQueueTelemetry _telemetry;

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly CancellationTokenSource _stopTokenSource;

    // Track active processing tasks to await them during disposal
    private readonly ConcurrentDictionary<Task, byte> _processingTasks = new();

    private Task? _consumerTask;
    private CancellationTokenRegistration _lifetimeRegistration;
    private readonly SemaphoreSlim _pauseLock = new SemaphoreSlim(1, 1);
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pauseCount = 0;  // Track pause state to prevent deadlock
    private volatile bool _isActive;
    private int _disposeState;
    private int _consumerGroupReleased;

    public InMemorySubscription(
        string topic,
        string consumerGroup,
        ConsumerGroupBuffer consumerGroupBuffer,
        Action releaseConsumerGroup,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options,
        IMessageSerializer serializer,
        MiddlewarePipeline pipeline,
        InMemoryOptions config,
        string queueName,
        IInMemoryDeadLetterStore deadLetterStore,
        MessageQueueTelemetry telemetry,
        ILogger logger)
    {
        _topic = topic;
        _consumerGroup = consumerGroup;
        _consumerGroupBuffer = consumerGroupBuffer ?? throw new ArgumentNullException(nameof(consumerGroupBuffer));
        _releaseConsumerGroup = releaseConsumerGroup ?? throw new ArgumentNullException(nameof(releaseConsumerGroup));
        _handler = handler;
        _options = options;
        _serializer = serializer;
        _pipeline = pipeline;
        _config = config;
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        _deadLetterStore = deadLetterStore ?? throw new ArgumentNullException(nameof(deadLetterStore));
        _telemetry = telemetry;
        _logger = logger;

        ValidateDeliveryPolicy(options, config);
        var maxConcurrency = options?.MaxConcurrency ?? 1;
        _concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _stopTokenSource = new CancellationTokenSource();

        SubscriptionId = Guid.NewGuid().ToString();
    }

    public string SubscriptionId { get; }

    public IReadOnlyList<string> Topics => new[] { _topic };

    public bool IsActive => _isActive && Volatile.Read(ref _disposeState) == 0;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_isActive)
        {
            throw new InvalidOperationException("Subscription is already active");
        }

        _isActive = true;
        _lifetimeRegistration = cancellationToken.UnsafeRegister(
            static state => _ = ((InMemorySubscription<TMessage>)state!).StopFromLifetimeAsync(),
            this);

        _consumerTask = ConsumeMessagesAsync(_stopTokenSource.Token);

        await Task.CompletedTask;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposeState) != 0) return;

        // Use Interlocked to prevent multiple pause calls from acquiring lock multiple times (deadlock prevention)
        if (Interlocked.CompareExchange(ref _pauseCount, 1, 0) == 0)
        {
            try
            {
                await _pauseLock.WaitAsync(cancellationToken);
                _isActive = false;
                _logger.LogInformation(InMemoryLogEvents.Subscription, "Subscription {SubscriptionId} paused for topic '{Topic}'", SubscriptionId, _topic);
            }
            catch
            {
                Interlocked.Exchange(ref _pauseCount, 0);
                throw;
            }
        }
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(InMemorySubscription<TMessage>));
        }

        // Use Interlocked to prevent multiple resume calls from releasing lock multiple times
        if (Interlocked.CompareExchange(ref _pauseCount, 0, 1) == 1)
        {
            _isActive = true;
            _pauseLock.Release();
            _logger.LogInformation(InMemoryLogEvents.Subscription, "Subscription {SubscriptionId} resumed for topic '{Topic}'", SubscriptionId, _topic);
        }

        return Task.CompletedTask;
    }

    private async Task ConsumeMessagesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug(InMemoryLogEvents.Subscription, "Started consuming messages for topic '{Topic}'", _topic);

        try
        {
            await foreach (var messageWrapper in _consumerGroupBuffer.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Wait if paused (blocks without consuming CPU)
                    await _pauseLock.WaitAsync(cancellationToken);
                    _pauseLock.Release();

                    if (!_isActive)
                    {
                        // Pause can win the race immediately after this reader passed the pause
                        // gate. Preserve the dequeued message for the next active consumer.
                        ReturnCanceledMessage(messageWrapper);
                        continue;
                    }

                    // Check routing pattern match
                    if (_options?.RoutingPatterns != null)
                    {
                        if (!RoutingPatternMatcher.MatchesAny(messageWrapper.RoutingKey, _options.RoutingPatterns))
                        {
                            continue; // Skip messages that don't match any pattern
                        }
                    }
                    else if (_options?.RoutingPattern != null)
                    {
                        if (!RoutingPatternMatcher.Matches(messageWrapper.RoutingKey, _options.RoutingPattern))
                        {
                            continue; // Skip messages that don't match the pattern
                        }
                    }
                    // If no routing pattern specified, accept all messages (default behavior)

                    // Enforce concurrency limit
                    await _concurrencySemaphore.WaitAsync(cancellationToken);

                    // Process message with retry logic (track task to await during disposal)
                    var processingTask = ProcessMessageAsync(messageWrapper, cancellationToken);
                    _processingTasks.TryAdd(processingTask, 0);
                    _ = ObserveProcessingTaskAsync(processingTask);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ReturnCanceledMessage(messageWrapper);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(InMemoryLogEvents.Subscription, "Message consumption canceled for topic '{Topic}'", _topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(InMemoryLogEvents.Processing, ex, "Error consuming messages for topic '{Topic}'", _topic);
        }
        finally
        {
            _isActive = false;
            ReleaseConsumerGroup();
        }
    }

    private async Task ProcessMessageAsync(MessageWrapper messageWrapper, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessMessageWithRetryAsync(messageWrapper, cancellationToken);
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task ObserveProcessingTaskAsync(Task processingTask)
    {
        try
        {
            await processingTask;
        }
        catch (OperationCanceledException) when (_stopTokenSource.IsCancellationRequested)
        {
            // Subscription lifetime cancellation is expected.
        }
        catch (Exception exception)
        {
            _logger.LogError(
                InMemoryLogEvents.Processing,
                exception,
                "Message processing task failed for subscription {SubscriptionId} on topic '{Topic}'",
                SubscriptionId,
                _topic);
        }
        finally
        {
            _processingTasks.TryRemove(processingTask, out _);
        }
    }

    private void ReleaseConsumerGroup()
    {
        if (Interlocked.Exchange(ref _consumerGroupReleased, 1) == 0)
        {
            _releaseConsumerGroup();
        }
    }

    private async Task StopFromLifetimeAsync()
    {
        _isActive = false;
        await _stopTokenSource.CancelAsync();
    }

    private async Task ProcessMessageWithRetryAsync(MessageWrapper wrapper, CancellationToken cancellationToken)
    {
        var serializedMessage = wrapper.SerializedMessage;
        var messageId = wrapper.MessageId;
        var expiresAt = wrapper.ExpiresAt;

        // Check TTL
        if (expiresAt.HasValue && DateTimeOffset.UtcNow > expiresAt.Value)
        {
            using var expiredDelivery = _telemetry.StartDelivery("in_memory", _topic, _options?.ConsumerGroup);
            _logger.LogWarning(
                InMemoryLogEvents.DeadLetter,
                "Message {MessageId} expired (TTL exceeded), sending to DLQ",
                messageId);

            try
            {
                await SendToDeadLetterQueueAsync(
                    messageId,
                    serializedMessage,
                    "TTL_EXPIRED",
                    0,
                    cancellationToken);
                expiredDelivery.Complete(MessageStatus.Reject);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ReturnCanceledMessage(wrapper);
                expiredDelivery.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                expiredDelivery.Unhandled(ex);
                throw;
            }

            return;
        }

        var maxDeliveryCount = ResolveMaxDeliveryCount(_options, _config);
        for (var attempt = 1; attempt <= maxDeliveryCount; attempt++)
        {
            using var delivery = _telemetry.StartDelivery("in_memory", _topic, _options?.ConsumerGroup);
            MessageResult result;

            try
            {
                var envelope = _serializer.Deserialize<MessageEnvelope<TMessage>>(serializedMessage);
                delivery.AttachEnvelope(envelope, attempt);

                var context = new MessageContext<TMessage>
                {
                    Envelope = envelope,
                    Topic = _topic,
                    ConsumerGroup = _options?.ConsumerGroup,
                    DeliveryCount = attempt,
                    CancellationToken = cancellationToken
                };

                result = await _pipeline.ExecuteConsumeAsync(context, _handler, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ReturnCanceledMessage(wrapper);
                delivery.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                if ((_options?.RequeueOnException ?? false) && attempt < maxDeliveryCount)
                {
                    var retryDelay = GetRetryDelay(attempt, _options, _config);
                    _logger.LogError(InMemoryLogEvents.Retry, ex,
                        "Exception processing message {MessageId} (Delivery {DeliveryCount}/{MaxDeliveryCount}); " +
                        "retrying after {Delay}ms because RequeueOnException is enabled",
                        messageId,
                        attempt,
                        maxDeliveryCount,
                        retryDelay.TotalMilliseconds);

                    try
                    {
                        await Task.Delay(retryDelay, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        ReturnCanceledMessage(wrapper);
                        delivery.Cancel();
                        throw;
                    }

                    delivery.Unhandled(ex);
                    continue;
                }

                _logger.LogError(InMemoryLogEvents.Processing, ex,
                    "Exception processing message {MessageId} on delivery {DeliveryCount}; sending to DLQ",
                    messageId,
                    attempt);

                try
                {
                    await SendToDeadLetterQueueAsync(
                        messageId,
                        serializedMessage,
                        "UNHANDLED_EXCEPTION",
                        attempt,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ReturnCanceledMessage(wrapper);
                    delivery.Cancel();
                    throw;
                }
                catch (Exception settlementException)
                {
                    delivery.Unhandled(settlementException);
                    throw;
                }

                delivery.Unhandled(ex);
                return;
            }

            try
            {
                switch (result.Status)
                {
                    case MessageStatus.Success:
                        _logger.LogTrace(
                            InMemoryLogEvents.Processing,
                            "Successfully processed message {MessageId} (Delivery: {DeliveryCount})",
                            messageId,
                            attempt);
                        delivery.Complete(MessageStatus.Success);
                        return;

                    case MessageStatus.Reject:
                        _logger.LogWarning(
                            InMemoryLogEvents.DeadLetter,
                            "Message {MessageId} rejected by handler: {Reason}, sending to DLQ",
                            messageId,
                            result.Reason);
                        await SendToDeadLetterQueueAsync(
                            messageId,
                            serializedMessage,
                            result.Reason ?? "REJECTED",
                            attempt,
                            cancellationToken);
                        delivery.Complete(MessageStatus.Reject);
                        return;

                    case MessageStatus.Retry when attempt < maxDeliveryCount:
                        var retryDelay = GetRetryDelay(attempt, _options, _config);
                        _logger.LogWarning(
                            InMemoryLogEvents.Retry,
                            "Message {MessageId} requested retry (Delivery {DeliveryCount}/{MaxDeliveryCount}); " +
                            "retrying after {Delay}ms. Reason: {Reason}",
                            messageId,
                            attempt,
                            maxDeliveryCount,
                            retryDelay.TotalMilliseconds,
                            result.Reason);
                        await Task.Delay(retryDelay, cancellationToken);
                        delivery.Complete(MessageStatus.Retry);
                        continue;

                    case MessageStatus.Retry:
                        _logger.LogWarning(
                            InMemoryLogEvents.DeadLetter,
                            "Message {MessageId} reached MaxDeliveryCount={MaxDeliveryCount}; sending to DLQ",
                            messageId,
                            maxDeliveryCount);
                        await SendToDeadLetterQueueAsync(
                            messageId,
                            serializedMessage,
                            "MAX_DELIVERY_COUNT_EXCEEDED",
                            attempt,
                            cancellationToken);
                        delivery.Complete(MessageStatus.Reject);
                        return;

                    default:
                        throw new InvalidOperationException($"Unknown message result status '{result.Status}'.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ReturnCanceledMessage(wrapper);
                delivery.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                delivery.Unhandled(ex);
                throw;
            }
        }
    }

    private static int ResolveMaxDeliveryCount(SubscribeOptions? options, InMemoryOptions providerOptions)
    {
        return options?.MaxDeliveryCount
            ?? (options?.RetryPolicy is { } policy
                ? checked(policy.MaxRetryAttempts + 1)
                : providerOptions.MaxRetryAttempts);
    }

    private static TimeSpan GetRetryDelay(
        int deliveryCount,
        SubscribeOptions? options,
        InMemoryOptions providerOptions)
    {
        if (options?.RetryPolicy is not { } policy)
        {
            return providerOptions.RetryDelay;
        }

        var multiplier = policy.UseExponentialBackoff
            ? Math.Pow(policy.BackoffMultiplier, Math.Max(0, deliveryCount - 1))
            : 1d;
        var ticks = (long)Math.Min(policy.InitialDelay.Ticks * multiplier, policy.MaxDelay.Ticks);
        var delay = TimeSpan.FromTicks(ticks);

        if (policy.UseJitter)
        {
            const double minimumFactor = 0.8d;
            const double jitterRange = 0.4d;
            var jitterSample = RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
            var jitteredTicks = delay.Ticks * (minimumFactor + (jitterSample * jitterRange));
            delay = TimeSpan.FromTicks((long)Math.Min(jitteredTicks, policy.MaxDelay.Ticks));
        }

        return delay;
    }

    private static void ValidateDeliveryPolicy(SubscribeOptions? options, InMemoryOptions providerOptions)
    {
        if (ResolveMaxDeliveryCount(options, providerOptions) < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDeliveryCount must be at least one.");
        }

        if ((options?.MaxConcurrency ?? 1) < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxConcurrency must be at least one.");
        }

        if (options?.RetryPolicy is not { } policy)
        {
            if (ResolveMaxDeliveryCount(options, providerOptions) > 1 && providerOptions.RetryDelay <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(providerOptions),
                    "InMemoryOptions.RetryDelay must be greater than zero.");
            }

            return;
        }

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

    private void ReturnCanceledMessage(MessageWrapper wrapper)
    {
        if (!_consumerGroupBuffer.TryReturn(wrapper))
        {
            _logger.LogCritical(
                InMemoryLogEvents.Processing,
                "Could not return canceled message {MessageId} to in-memory consumer group '{ConsumerGroup}'.",
                wrapper.MessageId,
                _consumerGroup);
        }
    }

    private async Task SendToDeadLetterQueueAsync(
        string messageId,
        byte[] serializedMessage,  // Changed from string to byte[] to match MessageWrapper type
        string reason,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_config.EnableDeadLetterQueue)
        {
            _logger.LogWarning(
                InMemoryLogEvents.DeadLetter,
                "Message {MessageId} failed but DLQ is disabled. Message lost. Reason: {Reason}",
                messageId, reason);
            return;
        }

        try
        {
            var deadLetterMessage = new InMemoryDeadLetterMessage(
                _queueName,
                _topic,
                messageId,
                serializedMessage,
                reason,
                attemptCount,
                DateTimeOffset.UtcNow
            );

            _deadLetterStore.Store(deadLetterMessage);


            _logger.LogWarning(
                InMemoryLogEvents.DeadLetter,
                "Sent message {MessageId} to DLQ (Reason: {Reason}, Attempts: {Attempts})",
                messageId, reason, attemptCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(InMemoryLogEvents.DeadLetter, ex, "Failed to send message {MessageId} to DLQ", messageId);
            throw;
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
            _logger.LogDebug(InMemoryLogEvents.Disposal, "Disposing subscription {SubscriptionId} for topic '{Topic}'", SubscriptionId, _topic);

            _isActive = false;

            await _stopTokenSource.CancelAsync();

            if (_consumerTask != null)
            {
                try
                {
                    await _consumerTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when task is cancelled
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(InMemoryLogEvents.Disposal, ex, "Consumer task threw exception during disposal for subscription {SubscriptionId}",
                        SubscriptionId);
                }
            }

            if (!_processingTasks.IsEmpty)
            {
                _logger.LogDebug(InMemoryLogEvents.Disposal, "Waiting for {Count} active processing tasks to complete for subscription {SubscriptionId}",
                    _processingTasks.Count, SubscriptionId);

                try
                {
                    await Task.WhenAll(_processingTasks.Keys);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug(InMemoryLogEvents.Disposal, "Processing tasks cancelled during disposal");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(InMemoryLogEvents.Disposal, ex, "Error waiting for processing tasks during disposal for subscription {SubscriptionId}",
                        SubscriptionId);
                }
            }

            await _lifetimeRegistration.DisposeAsync();
            ReleaseConsumerGroup();
            _concurrencySemaphore.Dispose();
            _stopTokenSource.Dispose();
            _pauseLock.Dispose();

            _logger.LogInformation(InMemoryLogEvents.Disposal, "Subscription {SubscriptionId} disposed", SubscriptionId);
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
            throw;
        }
    }
}
